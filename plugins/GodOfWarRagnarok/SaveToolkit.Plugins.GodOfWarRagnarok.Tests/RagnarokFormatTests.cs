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
        Assert.False(_plugin.Probe(MemorySaveSet.SingleFile(name, SyntheticGameplay(), title)).IsMatch);
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
        var bytes = SyntheticGameplay();
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
        var bytes = SyntheticGameplay();
        bytes[offset] ^= 1;
        Assert.Throws<SaveFormatException>(() => _plugin.Open(File(bytes)));
    }

    [Fact]
    public void RejectsTruncatedArraysInvalidIdsAndMissingWallet()
    {
        foreach (var offset in new[] { 0x442, 0x45E, 0x508 })
        {
            var bytes = SyntheticGameplay();
            Write(bytes, offset, uint.MaxValue);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(File(bytes)));
        }

        var unordered = SyntheticGameplay();
        BinaryPrimitives.WriteUInt64LittleEndian(unordered.AsSpan(0x44E), 0);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(File(unordered)));
        var missingId = SyntheticGameplay();
        BinaryPrimitives.WriteUInt64LittleEndian(missingId.AsSpan(0x462), 99);
        Assert.Throws<SaveFormatException>(() => _plugin.Open(File(missingId)));
        var noWallet = SyntheticGameplay();
        noWallet.AsSpan(0x500, 8).Clear();
        Assert.Throws<SaveFormatException>(() => _plugin.Open(File(noWallet)));
        var duplicated = SyntheticGameplay();
        duplicated.AsSpan(0x500, 8).CopyTo(duplicated.AsSpan(0x600));
        Assert.Throws<SaveFormatException>(() => _plugin.Open(File(duplicated)));
        Assert.Throws<SaveFormatException>(() => _plugin.Open(File(SyntheticGameplay()[..^1])));
    }

    [Fact]
    public void EveryTruncationAndForeignByteSequenceFailsWithoutParserExceptions()
    {
        var bytes = SyntheticGameplay();
        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = File(bytes[..length]);
            _ = _plugin.Probe(truncated);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(truncated));
        }

        var random = new Random(28330);
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var garbage = new byte[random.Next(0, 4096)];
            random.NextBytes(garbage);
            Assert.False(_plugin.Probe(File(garbage)).IsMatch);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(File(garbage)));
        }
    }

    [Fact]
    public void InvalidMetadataTextIsRejected()
    {
        var invalid = SyntheticGameplay();
        invalid[0x50] = 0xFF;
        Assert.Throws<SaveFormatException>(() => _plugin.Open(File(invalid)));
        invalid.AsSpan(0x50, 0x140).Fill((byte)'a');
        Assert.Throws<SaveFormatException>(() => _plugin.Open(File(invalid)));
    }

    [Fact]
    public void InvalidEditsAreAtomicAndOutputAndBlobAreIsolated()
    {
        var original = SyntheticGameplay();
        Write(original, 0x190, 10);
        using var document = _plugin.Open(File(original));
        var fields = Flatten(document.BuildView()).OfType<FieldNode>().ToArray();
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
        using var document = _plugin.Open(File(SyntheticGameplay()));
        _ = document.BuildView();
        var table = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("raw-page/1")));
        Assert.InRange(table.Rows.Count, 1, 259);
        foreach (var id in new[] { "wallet/-1", "wallet/99999999", "raw-page/-1", "raw-group/2", "directory/a", "raw-page/2147483647" })
        {
            Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(id));
        }

        var known = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("wallet/0")));
        Assert.Contains(Flatten(known).OfType<TextNode>(), n => n.Text == "Hacksilver");
        Assert.Contains(Flatten(known).OfType<FieldNode>(), n => n.Value.AsInteger() == 1234 && n.Confidence == Confidence.Inferred);
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
        Assert.All(Flatten(table).OfType<FieldNode>(), f => Assert.Equal(Confidence.Unknown, f.Confidence));
        Assert.Throws<SaveFormatException>(() => _plugin.Open(MemorySaveSet.SingleFile("userpreferences", bytes[..^1], "PPSA08330")));
        Assert.False(_plugin.Probe(File(bytes)).IsMatch);
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile File(byte[] bytes) => MemorySaveSet.SingleFile("GOWRSAVE0", bytes, "PPSA08330");

    internal static byte[] SyntheticGameplay()
    {
        var bytes = new byte[2048];
        Write(bytes, 8, 8);
        Write(bytes, 12, 0x00513040);
        Write(bytes, 16, 696);
        Write(bytes, 20, 0x008C0400);
        Write(bytes, 0x48, 35);
        Write(bytes, 0x4C, 0x00513040);
        bytes[0x50] = (byte)'A';
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x440), 0x61C0);
        Write(bytes, 0x442, 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x446), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x44E), 2);
        Write(bytes, 0x45E, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x462), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x500), 0x00004895C643225F);
        Write(bytes, 0x508, 1);
        Convert.FromHexString("9E5240D27406E85A").CopyTo(bytes, 0x514);
        Write(bytes, 0x51C, 1234);
        Write(bytes, bytes.Length - 4, uint.MaxValue);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    internal static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        IEnumerable<ViewNode> children = node switch
        {
            TabsNode tabs => tabs.Tabs.SelectMany(t => t.Children),
            SectionNode section => section.Children,
            TableNode table => table.Rows.SelectMany(r => r.Cells.Values),
            _ => [],
        };
        foreach (var child in children.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
