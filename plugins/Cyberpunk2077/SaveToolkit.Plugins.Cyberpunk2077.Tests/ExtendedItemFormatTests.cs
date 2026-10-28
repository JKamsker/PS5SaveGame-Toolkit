using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ExtendedItemFormatTests
{
    [Fact]
    public void EverySampleExtendedRecordHasExactBoundsAndUnityHasThreeChildParts()
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var container = CsavContainer.Parse(file.ReadAllBytes());
            var tree = CsavTree.Parse(container);
            var items = InventoryReader.Read(container, tree);
            var decoded = items.ToDictionary(item => item.NodeId, item =>
                ExtendedItemReader.Read(container.Payload.AsSpan(tree.Nodes[item.NodeId].Offset, tree.Nodes[item.NodeId].Size)));
            Assert.Equal(10, decoded.Count(pair => pair.Value.Root is not null));
            Assert.Equal(3, decoded[54].Root!.Children.Count);
            Assert.Equal("None", decoded[54].Root!.Appearance);
            Assert.Equal(2, decoded[55].Flags);
            Assert.Equal(0x7F7FFFFF, BitConverter.SingleToInt32Bits(decoded[54].Additional!.RequiredLevel));
        }
    }

    [Fact]
    public void IndependentExtendedFixtureRetainsFlagsUnknownsAndRejectsTruncationAndSuffix()
    {
        // Node + 16-byte identity + flags + creation + 16-byte additional + one leaf part.
        var bytes = new byte[91];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1); // seed != 2 => extended.
        bytes[20] = 0xA3; // Named low bits plus unknown high bits.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21), 123);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(25), 0x1122334455667788);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(33), 0xABCDEF12);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(37), 7);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(41), 42);
        bytes[57] = 0x84; // Packed UTF-8 -4.
        "None"u8.CopyTo(bytes.AsSpan(58));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(62), 99);
        bytes[70] = 0; // Zero child count.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(71), 0x12345678);
        var data = ExtendedItemReader.Read(bytes);
        Assert.Equal(0xA3, data.Flags);
        Assert.Equal(123U, data.CreationTime);
        Assert.Equal(0xABCDEF12U, data.Additional!.Unknown);
        Assert.Equal(42UL, data.Root!.Identity.TweakId);
        Assert.Equal(99UL, data.Root.AttachmentSlot);
        Assert.Equal(0x12345678U, data.Root.Unknown);
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<SaveFormatException>(() => ExtendedItemReader.Read(bytes.AsSpan(0, length)));
        Assert.Throws<SaveFormatException>(() => ExtendedItemReader.Read([.. bytes, 0]));
        bytes[70] = 0x7F; // Oversized packed child count.
        Assert.Throws<SaveFormatException>(() => ExtendedItemReader.Read(bytes));
    }

    [Fact]
    public void PackedStringsSupportUtf16AndRejectMalformedEncodingAndHugeLengths()
    {
        var position = 0;
        Assert.Equal("ä", ExtendedItemReader.ReadString([1, 0xE4, 0], ref position));
        Assert.Equal(3, position);
        Assert.Throws<SaveFormatException>(() => ReadString([0x81, 0xFF]));
        Assert.Throws<SaveFormatException>(() => ReadString([0x7F, 0x7F]));
        static string ReadString(byte[] bytes) { var start = 0; return ExtendedItemReader.ReadString(bytes, ref start); }
    }
}
