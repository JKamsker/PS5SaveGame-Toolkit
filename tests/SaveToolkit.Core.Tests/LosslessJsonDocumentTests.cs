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
}
