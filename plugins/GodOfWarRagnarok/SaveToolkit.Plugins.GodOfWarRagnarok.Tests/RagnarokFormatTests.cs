using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokFormatTests
{
    private readonly GodOfWarRagnarokPlugin _plugin = new();

    [Theory]
    [InlineData("data.bin", "PPSA08330")]
    [InlineData("GOWRSAVE0", "PPSA00000")]
    [InlineData("GOWRSAVE", "PPSA08330")]
    [InlineData("GOWRSAVE-1", "PPSA08330")]
    [InlineData("GOWRSAVE0.bak", "PPSA08330")]
    public void ProbeRequiresTitleFileNameAndLayout(string name, string title)
    {
        Assert.False(_plugin.Probe(MemorySaveSet.SingleFile(name, RagnarokTestSupport.LegacyGameplay(), title)).IsMatch);
    }

    [Theory]
    [InlineData("PPSA08329")]
    [InlineData("PPSA08330")]
    [InlineData("PPSA08331")]
    [InlineData("PPSA08332")]
    [InlineData("PPSA08333")]
    [InlineData("PPSA08334")]
    [InlineData("PPSA25043")]
    [InlineData("PPSA25044")]
    public void RegionalTitlesStillRequireObservedLayout(string title)
    {
        var bytes = RagnarokTestSupport.LegacyGameplay();
        Assert.True(_plugin.Probe(MemorySaveSet.SingleFile("GOWRSAVE65", bytes, title)).IsMatch);
        bytes[12] ^= 1;
        Assert.False(_plugin.Probe(MemorySaveSet.SingleFile("GOWRSAVE65", bytes, title)).IsMatch);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(0x48)]
    [InlineData(0x4C)]
    [InlineData(0x440)]
    public void CorruptKnownHeaderWordsAreRejected(int offset)
    {
        var bytes = RagnarokTestSupport.LegacyGameplay();
        bytes[offset] ^= 1;
        Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(bytes)));
    }

    [Fact]
    public void RejectsTruncatedArraysInvalidIdsAndMissingWallet()
    {
        foreach (var offset in new[] { 0x442, 0x45E, 0x508 })
        {
            var bytes = RagnarokTestSupport.LegacyGameplay();
            Write(bytes, offset, uint.MaxValue);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(bytes)));
        }

        var unordered = RagnarokTestSupport.LegacyGameplay();
        BinaryPrimitives.WriteUInt64LittleEndian(unordered.AsSpan(0x44E), 0);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(unordered)));
        var missingId = RagnarokTestSupport.LegacyGameplay();
        BinaryPrimitives.WriteUInt64LittleEndian(missingId.AsSpan(0x462), 99);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(missingId)));
        var noWallet = RagnarokTestSupport.LegacyGameplay();
        noWallet.AsSpan(0x500, 8).Clear();
        Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(noWallet)));
        var duplicated = RagnarokTestSupport.LegacyGameplay();
        duplicated.AsSpan(0x500, 8).CopyTo(duplicated.AsSpan(0x600));
        Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(duplicated)));
        Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(RagnarokTestSupport.LegacyGameplay()[..^1])));
    }

    [Fact]
    public void EveryTruncationAndForeignByteSequenceFailsWithoutParserExceptions()
    {
        var bytes = RagnarokTestSupport.LegacyGameplay();
        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = RagnarokTestSupport.GameplayFile(bytes[..length]);
            _ = _plugin.Probe(truncated);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(truncated));
        }

        var random = new Random(28330);
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var garbage = new byte[random.Next(0, 4096)];
            random.NextBytes(garbage);
            Assert.False(_plugin.Probe(RagnarokTestSupport.GameplayFile(garbage)).IsMatch);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(garbage)));
        }
    }

    [Fact]
    public void InvalidMetadataTextIsRejected()
    {
        var invalid = RagnarokTestSupport.LegacyGameplay();
        invalid[0x50] = 0xFF;
        Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(invalid)));
        invalid.AsSpan(0x50, 0x140).Fill((byte)'a');
        Assert.Throws<SaveFormatException>(() => _plugin.Open(RagnarokTestSupport.GameplayFile(invalid)));
    }

    [Fact]
    public void InvalidEditsAreAtomicAndOutputAndBlobAreIsolated()
    {
        var original = RagnarokTestSupport.LegacyGameplay();
        Write(original, 0x190, 10);
        using var document = _plugin.Open(RagnarokTestSupport.GameplayFile(original));
        var fields = RagnarokTestSupport.EagerNodes(document.BuildView()).OfType<FieldNode>().ToArray();
        Assert.True(document.CanEdit);
        Assert.All(fields.Where(f => f.ValueId != "metadata/play-seconds"), f => Assert.True(f.Spec.ReadOnly));
        var writable = Assert.Single(fields, field => field.ValueId == "metadata/play-seconds");
        Assert.False(writable.Spec.ReadOnly);
        var rejected = document.ApplyEdits([new(writable.ValueId, FieldValue.Of(0L)), new("unknown", FieldValue.Of(2L))]);
        Assert.False(rejected.Succeeded);
        Assert.Equal("unknown", Assert.Single(rejected.Errors).ValueId);
        Assert.Empty(rejected.Changes);
        Assert.Equal(writable.Value, document.GetValue(writable.ValueId));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var blob = document.ReadBlob("save", 0, 64);
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(blob, out var array));
        array.Array![array.Offset] ^= 1;
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var output = Assert.Single(document.Serialize()).Content;
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(output, out array));
        array.Array![array.Offset] ^= 1;
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("save", -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("save", 0, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("save", original.Length, 1));
        Assert.Empty(document.ReadBlob("save", original.Length, 0).ToArray());
        Assert.Throws<KeyNotFoundException>(() => document.ReadBlob("other", 0, 1));
    }

    [Fact]
    public void RawPagesAndLazyIdsAreBounded()
    {
        using var document = _plugin.Open(RagnarokTestSupport.GameplayFile(RagnarokTestSupport.LegacyGameplay()));
        _ = document.BuildView();
        var table = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("raw-page/1")));
        Assert.InRange(table.Rows.Count, 1, 259);
        foreach (var id in new[] { "wallet/-1", "wallet/99999999", "raw-page/-1", "raw-group/2", "directory/a", "raw-page/2147483647" })
        {
            Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(id));
        }

        var known = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("wallet/0")));
        Assert.Contains(RagnarokTestSupport.EagerNodes(known).OfType<TextNode>(), n => n.Text == "Hacksilver");
        Assert.Contains(RagnarokTestSupport.EagerNodes(known).OfType<FieldNode>(), n => n.Value.AsInteger() == 1234 && n.Confidence == Confidence.Inferred);
    }

    [Fact]
    public void PreferencesHaveSeparateExactLengthLayoutAndRawByteView()
    {
        var bytes = new byte[696];
        Write(bytes, 8, 31);
        Write(bytes, 12, 0x00513040);
        using var document = _plugin.Open(MemorySaveSet.SingleFile("userpreferences", bytes, "PPSA08330"));
        Assert.False(document.CanEdit);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var table = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("raw-page/0")));
        Assert.Equal(256, table.Rows.Count);
        Assert.Equal(184, Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("raw-page/2"))).Rows.Count);
        Assert.All(RagnarokTestSupport.EagerNodes(table).OfType<FieldNode>(), f => Assert.Equal(Confidence.Unknown, f.Confidence));
        Assert.Throws<SaveFormatException>(() => _plugin.Open(MemorySaveSet.SingleFile("userpreferences", bytes[..^1], "PPSA08330")));
        Assert.False(_plugin.Probe(RagnarokTestSupport.GameplayFile(bytes)).IsMatch);
    }

    private static void Write(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
