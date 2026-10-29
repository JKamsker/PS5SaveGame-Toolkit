using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithAllocatorBoundaryTests
{
    private const int SecondaryCapacity = 100;
    private const int RecordSize = 16;
    private const int IndexSize = sizeof(int);
    private const int KeySize = sizeof(ulong);
    private const int MetadataSize = 16;
    private const int FinalSizeAndPadding = 2 * sizeof(int);
    private const int KeyShift = 32;
    private const int SecondaryOffset = FaithInventory.Capacity * (RecordSize + IndexSize + KeySize) + MetadataSize + FinalSizeAndPadding;
    private readonly FinalFantasy16Plugin _plugin = new();
    private readonly FaithEvidenceDecoder _decoder = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothBanksRejectExtremeAndInconsistentAllocatorBounds(bool secondary)
    {
        var capacity = secondary ? SecondaryCapacity : FaithInventory.Capacity;
        var offset = secondary ? SecondaryOffset : 0;
        var metadata = offset + capacity * (RecordSize + IndexSize);
        var sizeOffset = metadata + MetadataSize + capacity * KeySize;
        (int Offset, ulong Value, bool Wide)[] corruptions =
        [
            (metadata, (ulong)capacity + 1, true), (metadata, ulong.MaxValue, true),
            (metadata + KeySize, uint.MaxValue, false), (metadata + KeySize, unchecked((uint)int.MinValue), false),
            (metadata + KeySize, int.MaxValue, false), (metadata + KeySize, (ulong)capacity + 1, false),
            (sizeOffset, uint.MaxValue, false), (sizeOffset, unchecked((uint)int.MinValue), false),
            (sizeOffset, int.MaxValue, false), (sizeOffset, secondary ? 1UL : 3UL, false),
        ];
        foreach (var corruption in corruptions)
        {
            var payload = FaithTestData.Items();
            if (corruption.Wide) BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(corruption.Offset), corruption.Value);
            else BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(corruption.Offset), (uint)corruption.Value);
            var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
            var file = FaithTestData.File(bytes);
            var error = Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
            Assert.Equal($"Faith inventory allocator bounds are invalid at 0x{FaithContainer.HeaderSize + FaithBlock.HeaderSize + offset:X}.",
                error.Message);
            var sink = new FaithDecodeTestSink();
            Assert.Throws<SaveFormatException>(() => _decoder.Decode(file, sink));
            Assert.Equal(0, sink.AddCalls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyAndFullBanksRespectExactCapacityAndRoundTrip(bool full)
    {
        var payload = new byte[FaithSchema.ItemLength];
        FillBank(payload, 0, FaithInventory.Capacity, full);
        FillBank(payload, SecondaryOffset, SecondaryCapacity, full);
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
        var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
        Assert.True(inventory.IsSupported);
        Assert.True(inventory.IsSecondarySupported);
        Assert.Equal(full ? FaithInventory.Capacity : 0, inventory.Items.Count);
        Assert.Equal(full ? SecondaryCapacity : 0, inventory.SecondaryItems.Count);
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var sink = new FaithDecodeTestSink();
        _decoder.Decode(FaithTestData.File(bytes), sink);
        Assert.Equal(inventory.Items.Count * 2, sink.AddCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnconfirmedFreeListsAndKeysUseTheDocumentedOpaqueFallback(bool secondary, bool unknownKey)
    {
        var capacity = secondary ? SecondaryCapacity : FaithInventory.Capacity;
        var offset = secondary ? SecondaryOffset : 0;
        var payload = new byte[FaithSchema.ItemLength];
        FillBank(payload, 0, FaithInventory.Capacity, true);
        FillBank(payload, SecondaryOffset, SecondaryCapacity, true);
        var metadata = offset + capacity * (RecordSize + IndexSize);
        if (unknownKey) BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(metadata + MetadataSize), ulong.MaxValue);
        else BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(metadata + KeySize), capacity - 1);
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload));
        var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
        Assert.Equal(secondary, inventory.IsSupported);
        Assert.Equal(!secondary, inventory.IsSecondarySupported);
        Assert.Equal(secondary ? FaithInventory.Capacity : 0, inventory.Items.Count);
        Assert.Equal(secondary ? 0 : SecondaryCapacity, inventory.SecondaryItems.Count);
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void FillBank(byte[] payload, int offset, int capacity, bool full)
    {
        if (!full) return;
        var metadata = offset + capacity * (RecordSize + IndexSize);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(metadata), (ulong)capacity);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(metadata + KeySize), capacity);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(metadata + MetadataSize + capacity * KeySize), capacity);
        for (var slot = 0; slot < capacity; slot++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + slot * RecordSize), slot + 1);
            BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(metadata + MetadataSize + slot * KeySize),
                (ulong)slot << KeyShift | (uint)slot);
        }
    }
}
