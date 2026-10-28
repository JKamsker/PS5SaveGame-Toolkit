using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed record FaithItem(int Slot, int Offset, int Id)
{
    public string QuantityId => $"inventory/{Slot}/quantity";
    public string Name => FaithItemNames.Find(Id) ?? $"Item {Id}";

    public bool HasKnownName => FaithItemNames.Find(Id) is not null;
}

internal sealed record FaithInventory(IReadOnlyList<FaithItem> Items, bool IsSupported)
{
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
        var blocks = container.Blocks.Where(FaithSchema.SupportsItems).ToArray();
        if (blocks.Length != 1)
        {
            return new([], false);
        }

        var payload = blocks[0].PayloadOffset;
        var count = ValidateAllocator(bytes, payload, Capacity);
        _ = ValidateAllocator(bytes, payload + AllocatorSize(Capacity), SecondaryCapacity);
        if (count < 0)
        {
            return new([], false);
        }
        var keyOffset = payload + Capacity * (RecordSize + IndexSize) + MetadataSize;
        var items = new List<FaithItem>(count);
        for (var slot = 0; slot < count; slot++)
        {
            // Only the dense identity-key arrangement has been substantiated in the PS5 corpus.
            // Do not interpret free-list slots or guess how a different arrangement maps records.
            var key = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(keyOffset + slot * KeySize)..]);
            var offset = payload + slot * RecordSize;
            var id = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            if (key != ((ulong)slot << KeyShift | (uint)slot) || id <= 0)
            {
                return new([], false);
            }

            items.Add(new FaithItem(slot, offset, id));
        }

        return new(items, true);
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
