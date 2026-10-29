using System.Buffers.Binary;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class InventoryItem(string name, int id, int quantityOffset, ushort quantity, bool equipped)
{
    public string Name { get; } = name;
    public int Id { get; } = id;
    public int QuantityOffset { get; } = quantityOffset;
    public ushort Quantity { get; set; } = quantity;
    public bool Equipped { get; } = equipped;
    public ushort OriginalQuantity { get; } = quantity;
    public string ValueId => $"inventory/{Id}/quantity";
    public uint Flags { get; init; }
    public float Durability { get; set; }
    public float OriginalDurability { get; init; }
    public int DurabilityOffset => QuantityOffset + sizeof(ushort);
    public string DurabilityId => $"inventory/{Id}/durability";
    public bool CanEditDurability => OriginalDurability is >= 0 and <= MaximumDurability;
    public const float MaximumDurability = 100;
    public string Slot { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<InventoryAttribute> Attributes { get; init; } = [];
    public IReadOnlyList<string> PrefixNames { get; init; } = [];
    public string OpaquePrefix { get; init; } = "";
    public int Offset { get; init; }
    public int Size { get; init; }
    public string OpaqueHash { get; init; } = "";
}

internal sealed record InventoryAttribute(string Name, uint RawValue, byte Type);

internal sealed record PlayerInventory(IReadOnlyList<InventoryItem> Items)
{
    public static PlayerInventory? Read(Sav3Node tail, byte[] data, IReadOnlyList<string> names)
    {
        var raw = data.AsSpan(tail.Offset, tail.Size).ToArray();
        // Only the observed PS5 native component at the structurally reached Entity tail is supported.
        // The prefix contains two opaque zero bytes, a presence byte and the component's hash.
        ReadOnlySpan<byte> prefix = [0, 0, 1, 0xA2, 0x15, 0x35, 0xB6];
        return raw.AsSpan().StartsWith(prefix) ? new InventoryReader(raw, names, tail.Offset).Read() : null;
    }

    public IEnumerable<Sav3Patch> Patches()
    {
        foreach (var item in Items)
        {
            if (item.Quantity != item.OriginalQuantity)
            {
                yield return new Sav3Patch(item.QuantityOffset, sizeof(ushort), BinaryData.EncodeUInt16(item.Quantity));
            }

            if (BitConverter.SingleToInt32Bits(item.Durability) != BitConverter.SingleToInt32Bits(item.OriginalDurability))
            {
                var bytes = new byte[sizeof(float)];
                BinaryPrimitives.WriteSingleLittleEndian(bytes, item.Durability);
                yield return new Sav3Patch(item.DurabilityOffset, bytes.Length, bytes);
            }
        }
    }
}
