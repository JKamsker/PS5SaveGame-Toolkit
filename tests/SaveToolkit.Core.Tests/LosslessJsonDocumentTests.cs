using System.Text;
using SaveToolkit.Sdk.Json;

namespace SaveToolkit.Core.Tests;

public sealed class LosslessJsonDocumentTests
{
    [Fact]
    public void NoOpSerializationPreservesBomTriviaDuplicatesAndLexemes()
    {
        var input = Encoding.UTF8.GetBytes("\uFEFF \r\n{\"x\":1.2300e+004,\"x\":\"\\u0041\",\"flag\":true,\"none\":null,\"rows\":[-0,2]} \t\r\n");
        var document = LosslessJsonDocument.Parse(input);

        Assert.Equal(input, document.Serialize());
        Assert.Equal("\uFEFF \r\n", Encoding.UTF8.GetString(document.Preamble.Span));
        Assert.Equal(" \t\r\n", Encoding.UTF8.GetString(document.Postamble.Span));
        var duplicate = document.Root.Children[1];
        Assert.Equal("x", duplicate.PropertyName);
        Assert.Equal(1, duplicate.PropertyOccurrence);
        Assert.Equal("\"x\"", duplicate.PropertyNameLexeme);
        Assert.Equal("\"x\"", Encoding.UTF8.GetString(input.AsSpan(duplicate.PropertyNameSpan!.Value.Offset, duplicate.PropertyNameSpan.Value.Length)));
        Assert.Equal("1.2300e+004", document.Root.Children[0].OriginalLexeme);
        Assert.Equal("A", duplicate.StringValue);
        Assert.True(document.Root.Children[2].BooleanValue);
        Assert.True(document.Root.Children[3].IsNull);
        Assert.Equal(LosslessJsonNodeKind.Array, document.Root.Children[4].Kind);
    }

    [Fact]
    public void ScalarPatchesChangeOnlyTheirOriginalSpansAndSupportTypeChanges()
    {
        const string source = "{ \"before\" : 123456, \"value\" : 1.2300e+004, \"after\" : \"keep\" }";
        var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes(source));
        var value = document.Root.Children[1];
        var after = document.Root.Children[2];

        Assert.True(document.ReplaceScalar(value, "null"));
        Assert.True(document.ReplaceScalar(after, "\"changed\""));
        var output = Encoding.UTF8.GetString(document.Serialize());
        Assert.Equal("{ \"before\" : 123456, \"value\" : null, \"after\" : \"changed\" }", output);
        Assert.Equal("1.2300e+004", value.OriginalLexeme);
        Assert.Equal("null", value.CurrentLexeme);
        Assert.Equal("changed", after.StringValue);
        Assert.False(document.ReplaceScalar(value, "null"));
    }

    [Fact]
    public void SnapshotRestoreReturnsRedoStateAndRestoresExactBytes()
    {
        var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("[1,2,3]"));
        var first = document.Root.Children[0];
        var second = document.Root.Children[1];
        var original = document.CaptureSnapshot();
        const string replacement = "1000000000000000000000000000000000000000000001";
        Assert.True(document.ReplaceScalar(first, replacement));
        var changed = document.CaptureSnapshot();
        Assert.True(document.ReplaceScalar(second, "false"));

        var redo = document.RestoreSnapshot(changed);
        Assert.Equal($"[{replacement},2,3]", Encoding.UTF8.GetString(document.Serialize()));
        document.RestoreSnapshot(original);
        Assert.Equal("[1,2,3]", Encoding.UTF8.GetString(document.Serialize()));
        document.RestoreSnapshot(redo);
        Assert.Equal($"[{replacement},false,3]", Encoding.UTF8.GetString(document.Serialize()));
        var other = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("[1,2,3]"));
        Assert.Throws<ArgumentException>(() => other.RestoreSnapshot(changed));
    }

    [Fact]
    public void MalformedInputAndLimitsAreRejectedWithoutBroadeningJsonGrammar()
    {
        Assert.Throws<LosslessJsonException>(() => LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("{\"x\":1,}")));
        Assert.Throws<LosslessJsonException>(() => LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("{\"x\":\"\\uD800\"}")));
        Assert.Throws<LosslessJsonException>(() => LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("{\"x\":1}{\"y\":2}")));
        Assert.Throws<LosslessJsonException>(() => LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("[1,2,3]"), new LosslessJsonLimits { MaxTokens = 2 }));
        var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("[1e1000000000]"));
        Assert.Equal("1e1000000000", document.Root.Children[0].OriginalLexeme);
        Assert.Throws<LosslessJsonException>(() => document.ReplaceScalar(document.Root.Children[0], "1e1000000000"));
    }

    [Fact]
    public void InvalidReplacementDoesNotMutateDocument()
    {
        var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("{\"x\":1}"));
        var node = document.Root.Children[0];
        Assert.Throws<LosslessJsonException>(() => document.ReplaceScalar(node, " 2"));
        Assert.Throws<LosslessJsonException>(() => document.ReplaceScalar(node, "\"\\uD800\""));
        Assert.Equal("{\"x\":1}", Encoding.UTF8.GetString(document.Serialize()));
    }

    [Fact]
    public void SerializedBudgetRejectsLengthGrowthBeforeMutatingState()
    {
        var bytes = Encoding.UTF8.GetBytes("[1]");
        var document = LosslessJsonDocument.Parse(bytes, new LosslessJsonLimits { MaxBytes = bytes.Length });
        var node = document.Root.Children[0];

        Assert.Throws<LosslessJsonException>(() => document.ReplaceScalar(node, "123456"));
        Assert.Equal("1", node.CurrentLexeme);
        Assert.Equal(bytes, document.Serialize());
    }

    [Fact]
    public void OversizedImportedNumberSurvivesNoOpRestoreAndOtherFieldUndoRedo()
    {
        const string source = "{\"huge\":1e1000000000,\"other\":1}";
        var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes(source));
        var huge = document.Root.Children[0];
        var other = document.Root.Children[1];
        var original = document.CaptureSnapshot();

        document.RestoreSnapshot(original);
        Assert.Equal(source, Encoding.UTF8.GetString(document.Serialize()));
        Assert.Equal("1e1000000000", huge.CurrentLexeme);

        Assert.True(document.ReplaceScalar(other, "2"));
        var changed = document.CaptureSnapshot();
        var redo = document.RestoreSnapshot(original);
        Assert.Equal(source, Encoding.UTF8.GetString(document.Serialize()));
        Assert.Equal("1e1000000000", huge.CurrentLexeme);
        document.RestoreSnapshot(changed);
        Assert.Equal("{\"huge\":1e1000000000,\"other\":2}", Encoding.UTF8.GetString(document.Serialize()));
        document.RestoreSnapshot(original);
        Assert.Equal(source, Encoding.UTF8.GetString(document.Serialize()));
        document.RestoreSnapshot(redo);
        Assert.Equal("{\"huge\":1e1000000000,\"other\":2}", Encoding.UTF8.GetString(document.Serialize()));
        document.RestoreSnapshot(original);
        Assert.Equal(source, Encoding.UTF8.GetString(document.Serialize()));
    }

    [Fact]
    public void ForeignNodeIsRejectedEvenWhenItsPreorderIdMatches()
    {
        var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("{\"x\":1}"));
        var equalSource = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("{\"x\":1}"));
        var differentSource = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes("{\"other\":9}"));

        Assert.Throws<ArgumentException>(() => document.ReplaceScalar(equalSource.Root.Children[0], "2"));
        Assert.Throws<ArgumentException>(() => document.ReplaceScalar(differentSource.Root.Children[0], "2"));
        Assert.Equal("{\"x\":1}", Encoding.UTF8.GetString(document.Serialize()));
        Assert.Equal("1", document.Root.Children[0].CurrentLexeme);
    }

    [Theory]
    [InlineData("\"unterminated")]
    [InlineData("\"\\u12G4\"")]
    [InlineData("tru")]
    [InlineData("1e+")]
    public void MalformedScalarReplacementUsesSdkExceptionAndLeavesAllStateUnchanged(string replacement)
    {
        var bytes = Encoding.UTF8.GetBytes("0");
        var document = LosslessJsonDocument.Parse(bytes);
        var node = document.Root;
        var snapshot = document.CaptureSnapshot();

        Assert.Throws<LosslessJsonException>(() => document.ReplaceScalar(node, replacement));
        Assert.Equal("0", node.CurrentLexeme);
        Assert.Equal(LosslessJsonNodeKind.Number, node.Kind);
        Assert.Equal(snapshot.Replacements, document.CaptureSnapshot().Replacements);
        Assert.Equal(bytes, document.Serialize());
    }

    [Theory]
    [InlineData("[?]", 1)]
    [InlineData("[\n?]", 2)]
    [InlineData("[\r\n?]", 3)]
    [InlineData("\uFEFF[\n?]", 5)]
    [InlineData("[\"é\",?]", 6)]
    public void SyntaxErrorsReportAbsoluteOriginalByteOffsets(string source, int expectedOffset)
    {
        var exception = Assert.Throws<LosslessJsonException>(() => LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes(source)));

        Assert.Equal(expectedOffset, exception.ByteOffset);
    }

    [Fact]
    public void InvalidUtf8WinsBeforeSyntaxDiagnostics()
    {
        var bytes = new byte[] { (byte)'[', 0xC3, (byte)'?', (byte)']' };

        var exception = Assert.Throws<LosslessJsonException>(() => LosslessJsonDocument.Parse(bytes));

        Assert.Equal(1, exception.ByteOffset);
        Assert.Contains("UTF-8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedExponentAndLexemeRemainReadableAcrossTypedUndoRedo()
    {
        var longNumber = new string('7', LosslessJsonLimits.Default.MaxNumberLength + 1);
        var source = $"{{\"exponent\":1e1000000000,\"long\":{longNumber},\"flag\":false,\"text\":\"old\"}}";
        var document = LosslessJsonDocument.Parse(Encoding.UTF8.GetBytes(source));
        var exponent = document.Root.Children[0];
        var longValue = document.Root.Children[1];
        var flag = document.Root.Children[2];
        var text = document.Root.Children[3];
        var original = document.CaptureSnapshot();

        Assert.True(document.ReplaceScalar(flag, "true"));
        Assert.True(document.ReplaceScalar(text, "\"new\""));
        var changed = document.CaptureSnapshot();
        document.RestoreSnapshot(original);
        Assert.Equal(source, Encoding.UTF8.GetString(document.Serialize()));
        Assert.Equal("1e1000000000", exponent.CurrentLexeme);
        Assert.Equal(longNumber, longValue.CurrentLexeme);
        Assert.False(flag.BooleanValue);
        Assert.Equal("old", text.StringValue);

        document.RestoreSnapshot(changed);
        Assert.True(flag.BooleanValue);
        Assert.Equal("new", text.StringValue);
        Assert.Equal("1e1000000000", exponent.CurrentLexeme);
        Assert.Equal(longNumber, longValue.CurrentLexeme);
        document.RestoreSnapshot(original);
        Assert.Equal(source, Encoding.UTF8.GetString(document.Serialize()));
    }

    [Fact]
    public void CumulativeUtf8BudgetIncludesBomTriviaAndSupportsShrinkRevert()
    {
        var source = Encoding.UTF8.GetBytes("\uFEFF [1,2] \r\n");
        var limits = new LosslessJsonLimits { MaxBytes = source.Length + 5 };
        var document = LosslessJsonDocument.Parse(source, limits);
        var first = document.Root.Children[0];
        var second = document.Root.Children[1];

        Assert.True(document.ReplaceScalar(first, "\"é\""));
        Assert.True(document.ReplaceScalar(first, "\"éé\""));
        Assert.Throws<LosslessJsonException>(() => document.ReplaceScalar(second, "\"ß\""));
        Assert.Equal("\"éé\"", first.CurrentLexeme);
        Assert.Equal("2", second.CurrentLexeme);
        Assert.Equal(source.Length + 5, document.Serialize().Length);

        Assert.True(document.ReplaceScalar(first, "1"));
        Assert.Equal(source, document.Serialize());
        Assert.True(document.ReplaceScalar(second, "\"ß\""));
        var output = document.Serialize();
        Assert.Equal(source.Length + 3, output.Length);
        var reopened = LosslessJsonDocument.Parse(output, limits);
        Assert.Equal("ß", reopened.Root.Children[1].StringValue);
    }
}
