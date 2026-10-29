using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonParsingCharacterizationTests
{
    private static readonly string[] Labels = ["$", "a", "a", "[0]", "[1]"];
    private static readonly int[] Occurrences = [1, 1, 2, 1, 1];
    [Theory]
    [InlineData("[\"\\uD800\",]", 1)]
    [InlineData("{\"\\uD800\":0,}", 1)]
    [InlineData("\uFEFF[\"\\uD800\",]", 4)]
    public void UnicodeDecodingPrecedesLaterSyntaxErrors(string source, int offset)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var expected = $"Invalid JSON string or Unicode escape at byte offset {offset}.";
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => JsonTokenTree.Parse(bytes)).Message);
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => JsonTokenValidation.Validate(bytes)).Message);
    }

    [Fact]
    public void WholeInputUtf8ValidationPrecedesTokenDecoding()
    {
        byte[] bytes = [.. "[\"\\uD800\"] "u8.ToArray(), 0xFF];
        var expected = $"Invalid UTF-8 text at byte offset {bytes.Length - 1}.";
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => JsonTokenTree.Parse(bytes)).Message);
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => JsonTokenValidation.Validate(bytes)).Message);
    }

    [Theory]
    [InlineData("[2,]", "Edited JSON failed token round-trip validation.")]
    [InlineData("[\"\\uD800\",]", "Invalid JSON string or Unicode escape at byte offset 1.")]
    public void StreamingComparisonRetainsMismatchAndUnicodeValidationOrder(string output, string message)
    {
        var model = JsonTokenTree.Parse("[1]");
        Assert.Equal(message, Assert.Throws<SaveFormatException>(() => JsonTokenValidation.Validate(Encoding.UTF8.GetBytes(output), model)).Message);
    }

    [Fact]
    public void TokenSpansRetainEscapedDuplicateNamesTriviaAndExactNumberFallback()
    {
        const string source = "\uFEFF \r\n{ \"\\u0061\" : -0 ,\n \"a\"\t: [1e999999999999999999999, \"\\uD83D\\uDE00\"] } \t";
        var bytes = Encoding.UTF8.GetBytes(source);
        var tree = JsonTokenTree.Parse(bytes);
        Assert.Equal(Labels, tree.Nodes.Select(n => n.Label));
        Assert.Equal(Occurrences, tree.Nodes.Select(n => n.Occurrence));
        Assert.Equal("\uFEFF \r\n", Encoding.UTF8.GetString(tree.Preamble.Span));
        Assert.Equal(" \t", Encoding.UTF8.GetString(tree.Postamble.Span));
        Assert.Equal("\"\\u0061\" : ", Encoding.UTF8.GetString(bytes.AsSpan(tree.Nodes[1].MemberOffset, tree.Nodes[1].Offset - tree.Nodes[1].MemberOffset)));
        Assert.Equal(" ,\n ", Encoding.UTF8.GetString(tree.Nodes[2].SeparatorBefore.Span));
        Assert.True(tree.Nodes[3].Spec!.ReadOnly);
        Assert.Equal(bytes, JsonTreeWriter.Write(tree.Clone()));
    }
}
