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
        Assert.IsType<ExactNumberSpec>(Assert.IsType<NullableSpec>(fields[2].Spec).Inner);
        var edits = new[] { new ValueEdit(fields[2].ValueId, FieldValue.ExactNumber("2.0")), new ValueEdit(fields[4].ValueId, FieldValue.Of("longer 日本語\n\"text")) };
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
        Assert.Equal(7, fields.Count);
        Assert.All(fields, f => Assert.False(f.Spec.ReadOnly));
        var result = document.ApplyEdits([new(fields[0].ValueId, FieldValue.ExactNumber("2")), new(fields[2].ValueId, FieldValue.Of("\uD800"))]);
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
        Assert.Equal("19999", document.GetValue(fields[^1].ValueId).AsExactNumber());
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
    public void EveryGlobalSnapshotUsesByteMinusOneAndTestedOfflineCharacterIsWritable()
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
            Assert.Equal("50", fields.Single(f => f.Label == "masterVolume").Value.AsExactNumber());
            Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
        }
        foreach (var file in files.Where(f => f.Directory.Name.StartsWith("Character", StringComparison.Ordinal)))
        {
            Assert.True(plugin.Probe(file).IsMatch);
            using var document = plugin.Open(file);
            Assert.True(document.CanEdit);
            Assert.Contains(Fields(document), f => !f.Spec.ReadOnly);
            Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
            using var json = JsonDocument.Parse(file.ReadAllBytes());
            Assert.True(json.RootElement.TryGetProperty("SerializeMeta", out _));
            Assert.True(json.RootElement.TryGetProperty("CharacterSaveV1", out _));
        }
    }

    internal static byte[] Encode(string text, bool xor = false) => Encoding.UTF8.GetBytes(text)
        .Select(b => xor ? (byte)(b ^ 1) : unchecked((byte)(b - 1))).ToArray();

    internal static ISaveDocument Open(byte[] bytes) => new XorJsonPlugin().Open(MemorySaveSet.SingleFile("save", bytes));

    internal static IReadOnlyList<FieldNode> Fields(ISaveDocument document) => ViewTraversal.Fields(document, checkChildBound: true);
}
