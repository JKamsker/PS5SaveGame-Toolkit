using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed record FaithItem(int Slot, int Offset, int Id)
{
    public string QuantityId => $"inventory/{Slot}/quantity";
}

internal sealed record FaithInventory(IReadOnlyList<FaithItem> Items, bool IsSupported)
{
    public IReadOnlyList<FaithItem> SecondaryItems { get; init; } = [];
    public bool IsSecondarySupported { get; init; }
    public const int Capacity = 1200;
    private const int SecondaryCapacity = 100;
    private const int RecordSize = 16;
    private const int IndexSize = 4;
    private const int KeySize = 8;
    private const int MetadataSize = 16;
    private const int FinalSizeAndPadding = 8;
    private const int KeyShift = 32;

    public static FaithInventory Parse(ReadOnlySpan<byte> bytes, FaithContainer container)
    {
        var block = FaithSchema.Unique(container, FaithSchema.ItemType, FaithSchema.SupportsItems);
        if (block is null)
        {
            return new([], false);
        }

        var payload = block.PayloadOffset;
        var count = ValidateAllocator(bytes, payload, Capacity);
        var secondaryOffset = payload + AllocatorSize(Capacity);
        var secondaryCount = ValidateAllocator(bytes, secondaryOffset, SecondaryCapacity);
        var secondary = ReadDenseBank(bytes, secondaryOffset, SecondaryCapacity, secondaryCount, allowZero: true);
        var items = ReadDenseBank(bytes, payload, Capacity, count, allowZero: false);
        return new(items ?? [], items is not null)
        {
            SecondaryItems = secondary ?? [],
            IsSecondarySupported = secondary is not null,
        };
    }

    private static IReadOnlyList<FaithItem>? ReadDenseBank(ReadOnlySpan<byte> bytes, int payload,
        int capacity, int count, bool allowZero)
    {
        if (count < 0)
        {
            return null;
        }
        var keyOffset = payload + capacity * (RecordSize + IndexSize) + MetadataSize;
        var items = new List<FaithItem>(count);
        for (var slot = 0; slot < count; slot++)
        {
            // Only the dense identity-key arrangement has been substantiated in the PS5 corpus.
            // Do not interpret free-list slots or guess how a different arrangement maps records.
            var key = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(keyOffset + slot * KeySize)..]);
            var offset = payload + slot * RecordSize;
            var id = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            if (key != ((ulong)slot << KeyShift | (uint)slot) || id < 0 || (!allowZero && id == 0))
            {
                return null;
            }

            items.Add(new FaithItem(slot, offset, id));
        }

        return items;
    }

    private static int ValidateAllocator(ReadOnlySpan<byte> bytes, int offset, int capacity)
    {
        var metadata = offset + capacity * (RecordSize + IndexSize);
        var initialized = BinaryPrimitives.ReadUInt64LittleEndian(bytes[metadata..]);
        var next = BinaryPrimitives.ReadInt32LittleEndian(bytes[(metadata + sizeof(ulong))..]);
        var size = BinaryPrimitives.ReadInt32LittleEndian(bytes[(metadata + MetadataSize + capacity * KeySize)..]);
        if (initialized > (ulong)capacity || next < 0 || next > capacity || size < 0 || size > (int)initialized)
        {
            throw new SaveFormatException($"Faith inventory allocator bounds are invalid at 0x{offset:X}.");
        }

        // Unknown allocation/free-list arrangements remain available in the raw explorer.
        return initialized == (ulong)size && next == size ? size : -1;
    }

    private static int AllocatorSize(int capacity) =>
        capacity * (RecordSize + IndexSize + KeySize) + MetadataSize + FinalSizeAndPadding;
}
