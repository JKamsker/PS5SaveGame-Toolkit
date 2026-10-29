using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherFormatTests
{
    [Fact]
    public void TwoChunkFixtureResolvesVirtualOffsetsAndNestedPrimitives()
    {
        var bytes = SyntheticSave.Create();
        var container = WitcherContainer.Parse(bytes);
        Assert.Equal(2, container.Chunks.Count);
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        Assert.Equal(4, directory.Entries.Count);
        var count = Assert.Single(tree.Nodes, node => node.Offset == SyntheticSave.ScalarOffset);
        Assert.Equal(FieldValue.Of(123L), ScalarReader.Read(count, container.Payload, directory.Names)!.Value);
        var label = Assert.Single(tree.Nodes, node => node.Name == "label");
        Assert.Equal(FieldValue.Of("hello"), ScalarReader.Read(label, container.Payload, directory.Names)!.Value);
        var enabled = Assert.Single(tree.Nodes, node => node.Name == "enabled");
        Assert.Equal(FieldValue.Of(true), ScalarReader.Read(enabled, container.Payload, directory.Names)!.Value);
        var block = Assert.Single(tree.Nodes, node => node.Tag == "BLCK");
        Assert.Equal(3, block.Children.Count);
        using var document = WitcherTestFiles.Open(bytes);
        Assert.False(document.CanEdit);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, int.MaxValue)]
    [InlineData(12, 0)]
    [InlineData(16, int.MaxValue)]
    [InlineData(20, int.MaxValue)]
    [InlineData(24, 1)]
    [InlineData(28, int.MaxValue)]
    public void RejectsInvalidChunkHeader(int offset, int value)
    {
        var bytes = SyntheticSave.Create();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        Assert.Throws<SaveFormatException>(() => WitcherTestFiles.Open(bytes));
    }

    [Theory]
    [InlineData(0)] // SAV3 magic.
    [InlineData(SyntheticSave.ScalarOffset + 2)] // Name dictionary reference.
    [InlineData(SyntheticSave.ArchiveOffset + 2)] // SS length.
    [InlineData(SyntheticSave.BlockOffset + 6)] // BLCK length.
    [InlineData(SyntheticSave.BlockOffset + 10 + 8)] // AVAL length.
    public void RejectsInvalidInnerStructure(int offset)
    {
        var bytes = SyntheticSave.Create(payload => BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), int.MaxValue));
        Assert.Throws<SaveFormatException>(() => WitcherTestFiles.Open(bytes));
    }

    [Fact]
    public void RejectsFooterDirectoryCrossingAndNameTerminatorCorruption()
    {
        Action<byte[]>[] mutations = [
            payload => payload[^1] = 0,
            payload => BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(payload.Length - 6), int.MaxValue),
            payload => {
                var table = BinaryData.Int32(payload, payload.Length - 6) - SyntheticSave.HeaderSize;
                payload[table - 11] = 0; // ENOD marker.
            },
            payload => {
                var table = BinaryData.Int32(payload, payload.Length - 6) - SyntheticSave.HeaderSize;
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(table), int.MaxValue);
            },
            payload => {
                var table = BinaryData.Int32(payload, payload.Length - 6) - SyntheticSave.HeaderSize;
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(table + 4 + 8 + 4), 100); // Child crosses root.
            },
        ];
        foreach (var mutation in mutations)
        {
            Assert.Throws<SaveFormatException>(() => WitcherTestFiles.Open(SyntheticSave.Create(mutation)));
        }
    }

    [Fact]
    public void TruncatedCompressedDataIsRejected()
    {
        var bytes = SyntheticSave.Create();
        for (var length = 0; length < bytes.Length - 2; length++)
        {
            Assert.Throws<SaveFormatException>(() => WitcherTestFiles.Open(bytes[..length]));
        }

        bytes[SyntheticSave.HeaderSize] = 0;
        bytes[SyntheticSave.HeaderSize + 1] = 0;
        bytes[SyntheticSave.HeaderSize + 2] = 0;
        Assert.Throws<SaveFormatException>(() => WitcherTestFiles.Open(bytes));
    }

    [Fact]
    public void ProbeRequiresMatchingTitleFilenameAndMagic()
    {
        var plugin = new Witcher3Plugin();
        var bytes = SyntheticSave.Create();
        Assert.True(plugin.Probe(MemorySaveSet.SingleFile("Manual.sav", bytes, "PPSA04018")).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("Manual.sav", bytes, "PPSA04027")).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("user.settings", bytes, "PPSA04018")).IsMatch);
    }
}
