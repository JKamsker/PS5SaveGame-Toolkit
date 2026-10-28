using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonEditingTests
{
    private readonly GenericJsonPlugin _plugin = new();

    [Fact]
    public void PatchesOnlyChangedTokens_AndUndoRestoresOriginalSpelling()
    {
        const string source = "\uFEFF{\r\n \"x\": -0, \"escaped\": \"\\u0041\", \"x\": 1.2300E+02, \"s\": \"é\", \"b\":true }\t\n";
        using var document = Open(source);
        var fields = Fields(document);
        Assert.Equal(2, fields.Count(f => f.Label == "x"));
        Assert.IsType<NullableSpec>(fields[0].Spec);
        Assert.IsType<NullableSpec>(fields[2].Spec);
        var edits = new[] { new ValueEdit(fields[2].ValueId, Number("2.0")), new ValueEdit(fields[3].ValueId, Text("longer\n\"text")) };
        Assert.True(document.ApplyEdits(edits).Succeeded);
        var encodedString = System.Text.Json.JsonSerializer.Serialize("longer\n\"text");
        var expected = source.Replace("1.2300E+02", "2.0", StringComparison.Ordinal).Replace("\"é\"", encodedString, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), document.Serialize()[0].Content.ToArray());
        using var reopened = Open(expected);
        Assert.Equal("2.0", Fields(reopened)[2].Value.AsUnion().Value.AsExactNumber());
        Assert.True(document.ApplyEdits([new ValueEdit(fields[2].ValueId, fields[2].Value), new ValueEdit(fields[3].ValueId, fields[3].Value)]).Succeeded);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("{\"x\":01}")]
    [InlineData("{\"x\":true,}")]
    [InlineData("/*comment*/{}")]
    [InlineData("{} {}")]
    [InlineData("[1,")]
    [InlineData("\"\\uD800\"")]
    public void InvalidJsonIsRejected(string source)
    {
        var file = MemorySaveSet.SingleFile("settings.json", Encoding.UTF8.GetBytes(source));
        Assert.False(_plugin.Probe(file).IsMatch);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
    }

    [Fact]
    public void InvalidUtf8AndBinaryAreRejected()
    {
        foreach (var bytes in new byte[][] { [0x22, 0xFF, 0x22], [0xFF, 0xFE, 0x7B, 0], [0, 1, 2, 3] })
        {
            var file = MemorySaveSet.SingleFile("settings.json", bytes);
            Assert.False(_plugin.Probe(file).IsMatch);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
        }
    }

    [Fact]
    public void HandlesNullRootsNumericLimitsAndDuplicateNames()
    {
        using var document = Open("[null,{},[],true,1e9999,1e-9999,170141183460469231731687303715884105728,-170141183460469231731687303715884105728,1e0]");
        var fields = Fields(document);
        Assert.Equal(7, fields.Count);
        Assert.DoesNotContain(fields, f => f.Spec.ReadOnly);
        Assert.Equal(Int128.MinValue.ToString(), fields[5].Value.AsUnion().Value.AsExactNumber());
        Assert.Equal("1e0", fields[6].Value.AsUnion().Value.AsExactNumber());
        foreach (var root in new[] { "null", "{}", "[]", "true", "12", "\"text\"" })
        {
            using var scalar = Open(root);
            Assert.Equal(Encoding.UTF8.GetBytes(root), scalar.Serialize()[0].Content.ToArray());
        }
    }

    [Fact]
    public void InvalidBatchIsAtomicAndDoesNotChangeBytes()
    {
        const string source = "[1,2.0,\"text\"]";
        using var document = Open(source);
        var fields = Fields(document);
        Assert.False(document.ApplyEdits([new ValueEdit(fields[0].ValueId, Number("9")), new ValueEdit(fields[1].ValueId, FieldValue.Of(double.NaN))]).Succeeded);
        Assert.False(document.ApplyEdits([new ValueEdit(fields[2].ValueId, Text("\uD800"))]).Succeeded);
        Assert.False(document.ApplyEdits([new ValueEdit(fields[0].ValueId, FieldValue.Of("wrong type"))]).Succeeded);
        Assert.Equal(Encoding.UTF8.GetBytes(source), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void LargeFlatAndNestedViewsAreBoundedAndIdsSurviveLengthChanges()
    {
        var source = "[" + string.Join(',', Enumerable.Repeat("{\"a\":1}", 20_000)) + "]";
        using var document = Open(source);
        var fields = Fields(document);
        Assert.Equal(20_000, fields.Count);
        Assert.Equal(fields.Count, fields.Select(f => f.ValueId).Distinct().Count());
        Assert.True(document.ApplyEdits([new ValueEdit(fields[^1].ValueId, Number("123456"))]).Succeeded);
        using var reopened = _plugin.Open(MemorySaveSet.SingleFile("any.bin", document.Serialize()[0].Content.ToArray()));
        Assert.Equal(fields[^1].ValueId, Fields(reopened)[^1].ValueId);
    }

    [Fact]
    public void DepthAndSizeLimitsFailPredictably()
    {
        var file = MemorySaveSet.SingleFile("data.bin", Encoding.UTF8.GetBytes(new string('[', 65) + "0" + new string(']', 65)));
        Assert.False(_plugin.Probe(file).IsMatch);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
        var large = MemorySaveSet.SingleFile("data.bin", new byte[16 * 1024 * 1024 + 1]);
        Assert.False(_plugin.Probe(large).IsMatch);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(large));
        var many = MemorySaveSet.SingleFile("data.bin", Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat("0", 250_000)) + "]"));
        Assert.False(_plugin.Probe(many).IsMatch);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(many));
    }

    internal static FieldValue Number(string text) => FieldValue.Union("number", FieldValue.ExactNumber(text));
    internal static FieldValue Text(string text) => FieldValue.Union("string", FieldValue.Of(text));

    private ISaveDocument Open(string source) => _plugin.Open(MemorySaveSet.SingleFile("any.bin", Encoding.UTF8.GetBytes(source)));

    internal static IReadOnlyList<FieldNode> Fields(ISaveDocument document)
    {
        var fields = new List<FieldNode>();
        Visit(document.BuildView());
        return fields;

        void Visit(ViewNode node)
        {
            switch (node)
            {
                case FieldNode field:
                    fields.Add(field);
                    break;
                case TabsNode tabs:
                    foreach (var child in tabs.Tabs.SelectMany(t => t.Children)) Visit(child);
                    break;
                case SectionNode section:
                    var children = section.LazyId is { } id ? document.ExpandNode(id) : section.Children;
                    Assert.True(children.Count <= 128);
                    foreach (var child in children) Visit(child);
                    break;
            }
        }
    }
}
