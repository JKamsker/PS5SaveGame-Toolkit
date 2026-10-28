using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class XorJsonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyChangedTokensArePatchedAndUndoRestoresExactBytes(bool xor)
    {
        const string original = "\uFEFF{\r\n \"n\":-0, \"s\":\"\\u0041\", \"n\":1.2300E+02, \"t\":true, \"s\":\"é\" }\t\n";
        var bytes = Encode(original, xor);
        using var document = Open(bytes);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        var fields = Fields(document);
        Assert.Equal(5, fields.Count);
        Assert.IsType<FloatSpec>(fields[2].Spec);
        var edits = new[] { new ValueEdit(fields[2].ValueId, FieldValue.Of(2.0)), new ValueEdit(fields[4].ValueId, FieldValue.Of("longer 日本語\n\"text")) };
        Assert.True(document.ApplyEdits(edits).Succeeded);
        var expected = original.Replace("1.2300E+02", "2.0", StringComparison.Ordinal)
            .Replace("\"é\"", JsonSerializer.Serialize(edits[1].Value.AsString()), StringComparison.Ordinal);
        var changed = document.Serialize()[0].Content.ToArray();
        Assert.Equal(Encode(expected, xor), changed);
        using var reopened = Open(changed);
        var reopenedFields = Fields(reopened);
        Assert.Equal(fields.Select(f => f.ValueId), reopenedFields.Select(f => f.ValueId));
        Assert.Equal(edits[1].Value, reopened.GetValue(edits[1].ValueId));
        Assert.True(document.ApplyEdits([new(fields[2].ValueId, fields[2].Value), new(fields[4].ValueId, fields[4].Value)]).Succeeded);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData("{\"x\":01}")]
    [InlineData("[1,")]
    [InlineData("{} {}")]
    [InlineData("{\"x\":true,}")]
    [InlineData("[\"\\uD800\"]")]
    public void InvalidEncodedJsonIsRejected(string text)
    {
        foreach (var xor in new[] { false, true })
        {
            var file = MemorySaveSet.SingleFile("save", Encode(text, xor));
            Assert.False(new XorJsonPlugin().Probe(file).IsMatch);
            Assert.Throws<SaveFormatException>(() => new XorJsonPlugin().Open(file));
        }
    }

    [Fact]
    public void InvalidBatchIsAtomicAndLimitsAndNumericTypesArePreserved()
    {
        var bytes = Encode("[1,2.0,\"text\",1e9999,1e-9999,170141183460469231731687303715884105728,null]", false);
        using var document = Open(bytes);
        var fields = Fields(document);
        Assert.Equal(6, fields.Count);
        Assert.All(fields.Skip(3), f => Assert.True(f.Spec.ReadOnly));
        var result = document.ApplyEdits([new(fields[0].ValueId, FieldValue.Of(2L)), new(fields[2].ValueId, FieldValue.Of("\uD800"))]);
        Assert.False(result.Succeeded);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        foreach (var invalid in new[] { Encode(new string('[', 65) + "0" + new string(']', 65), true), new byte[16 * 1024 * 1024 + 1], new byte[] { 0x5A, 0xFE, 0x5C } })
            Assert.Throws<SaveFormatException>(() => Open(invalid));
    }

    [Fact]
    public void LargeTreesAreLazyAndAllValuesRemainAddressable()
    {
        var text = "[" + string.Join(',', Enumerable.Range(0, 20000)) + "]";
        using var document = Open(Encode(text, false));
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var lazy = Assert.IsType<SectionNode>(root.Tabs[1].Children[0]);
        Assert.Empty(lazy.Children);
        var fields = Fields(document);
        Assert.Equal(20000, fields.Count);
        Assert.Equal((Int128)19999, document.GetValue(fields[^1].ValueId).AsInteger());
    }

    [Fact]
    public void AmbiguousEncodingsAndPlainJsonAreDeclined()
    {
        foreach (var bytes in new[] { Encode("[]", true), "{\"value\":1}"u8.ToArray() })
        {
            var file = MemorySaveSet.SingleFile("ue4savegame.dpx.sav", bytes, "PPSA16064");
            Assert.False(new XorJsonPlugin().Probe(file).IsMatch);
            Assert.Throws<SaveFormatException>(() => new XorJsonPlugin().Open(file));
        }
    }

    [Fact]
    public void EveryGlobalSnapshotUsesByteMinusOneAndCharacterRemainsPlainJson()
    {
        var files = SampleSaves.Load("PPSA16064").AllDiskFiles.Where(f => !f.IsSystemFile).ToArray();
        var global = files.Where(f => f.Directory.Name == "GlobalSaveDataDefault").ToArray();
        Assert.NotEmpty(global);
        var plugin = new XorJsonPlugin();
        foreach (var file in global)
        {
            var bytes = file.ReadAllBytes();
            Assert.Equal(3978, bytes.Length);
            var plain = bytes.Select(b => unchecked((byte)(b + 1))).ToArray();
            using var json = JsonDocument.Parse(plain);
            Assert.True(json.RootElement.TryGetProperty("blobs", out _));
            Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(bytes.Select(b => (byte)(b ^ 1)).ToArray()));
            Assert.Contains("minus one", plugin.Probe(file).Label!);
            using var document = plugin.Open(file);
            var fields = Fields(document);
            Assert.Equal((Int128)50, fields.Single(f => f.Label == "masterVolume").Value.AsInteger());
            Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        }
        foreach (var file in files.Where(f => f.Directory.Name.StartsWith("Character", StringComparison.Ordinal)))
        {
            Assert.False(plugin.Probe(file).IsMatch);
            using var json = JsonDocument.Parse(file.ReadAllBytes());
            Assert.True(json.RootElement.TryGetProperty("SerializeMeta", out _));
            Assert.True(json.RootElement.TryGetProperty("CharacterSaveV1", out _));
        }
    }

    private static byte[] Encode(string text, bool xor) => Encoding.UTF8.GetBytes(text)
        .Select(b => xor ? (byte)(b ^ 1) : unchecked((byte)(b - 1))).ToArray();

    private static ISaveDocument Open(byte[] bytes) => new XorJsonPlugin().Open(MemorySaveSet.SingleFile("save", bytes));

    internal static IReadOnlyList<FieldNode> Fields(ISaveDocument document)
    {
        var fields = new List<FieldNode>();
        Visit(document.BuildView());
        return fields;
        void Visit(ViewNode node)
        {
            if (node is FieldNode field) fields.Add(field);
            if (node is TabsNode tabs) foreach (var child in tabs.Tabs.SelectMany(t => t.Children)) Visit(child);
            if (node is SectionNode section)
            {
                var children = section.LazyId is { } id ? document.ExpandNode(id) : section.Children;
                Assert.True(children.Count <= 128);
                foreach (var child in children) Visit(child);
            }
        }
    }
}
