using System.Buffers.Binary;
using SaveToolkit.Sdk;

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
    private const uint EquippedFlag = 0x200000;
    private const int MaximumItems = 10_000;
    private const int RecordPrefixSize = 24;
    private const int QuantityPosition = 17;
    private const int AttributeSize = 7;

    public static PlayerInventory? Read(Sav3Node tail, byte[] data, IReadOnlyList<string> names)
    {
        var raw = data.AsSpan(tail.Offset, tail.Size).ToArray();
        // Only the observed PS5 native component at the structurally reached Entity tail is supported.
        // The prefix contains two opaque zero bytes, a presence byte and the component's hash.
        ReadOnlySpan<byte> prefix = [0, 0, 1, 0xA2, 0x15, 0x35, 0xB6];
        if (!raw.AsSpan().StartsWith(prefix))
        {
            return null;
        }

        const int classOffset = 7;
        Need(classOffset, 6);
        if (Name(classOffset) != "CInventoryComponent")
        {
            throw new SaveFormatException("Invalid player inventory class index.");
        }

        var nextId = BinaryData.UInt16(raw, classOffset + 2);
        var count = BinaryData.UInt16(raw, classOffset + 4);
        if (count > MaximumItems || count > nextId)
        {
            throw new SaveFormatException("Invalid player inventory item count.");
        }

        var position = classOffset + 6;
        var ids = new HashSet<int>();
        var items = new List<InventoryItem>(count);
        for (var index = 0; index < count; index++)
        {
            var start = position;
            Need(position, RecordPrefixSize);
            var name = Name(position);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(position + 2));
            string[] prefixNames = [Name(position + 13, allowZero: true), Name(position + 15, allowZero: true)];
            var quantityOffset = position + QuantityPosition;
            var quantity = BinaryData.UInt16(raw, quantityOffset);
            var durability = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(position + 19));
            var attributes = raw[position + 23];
            if (quantity == 0 || !float.IsFinite(durability) || attributes > 32)
            {
                throw new SaveFormatException("Invalid player inventory quantity, durability or attributes.");
            }

            position += RecordPrefixSize;
            var itemAttributes = new List<InventoryAttribute>(attributes);
            for (var attribute = 0; attribute < attributes; attribute++)
            {
                Need(position, AttributeSize);
                itemAttributes.Add(new InventoryAttribute(Name(position), BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(position + 2)), raw[position + 6]));
                position += AttributeSize;
            }

            Need(position, 4);
            var id = raw[position] | raw[position + 1] << 8 | raw[position + 2] << 16;
            var tags = raw[position + 3];
            if (id < 1 || id > nextId || !ids.Add(id) || tags > 128)
            {
                throw new SaveFormatException("Invalid player inventory item ID or tag count.");
            }

            position += 4;
            var itemTags = new List<string>(tags);
            for (var tag = 0; tag < tags; tag++)
            {
                itemTags.Add(Name(position));
                position += 2;
            }

            var equipped = (flags & EquippedFlag) != 0;
            var slot = "";
            if (equipped)
            {
                slot = Name(position);
                position += 2;
            }

            items.Add(new InventoryItem(name, id, tail.Offset + quantityOffset, quantity, equipped) {
                Flags = flags, Durability = durability, OriginalDurability = durability, Slot = slot, Tags = itemTags, Attributes = itemAttributes,
                PrefixNames = prefixNames, OpaquePrefix = Convert.ToHexString(raw.AsSpan(start + 6, 7)),
                Offset = tail.Offset + start, Size = position - start,
                OpaqueHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw.AsSpan(start + 6, 7))),
            });
        }

        ReadOnlySpan<byte> terminal = [1, 0, 0, 0, 0, 0];
        if (!raw.AsSpan(position).SequenceEqual(terminal))
        {
            throw new SaveFormatException("Unsupported player inventory terminal or trailing data.");
        }

        return new PlayerInventory(items);

        void Need(int offset, int size) => _ = BinaryData.Slice(raw, offset, size);
        string Name(int offset, bool allowZero = false)
        {
            var index = BinaryData.UInt16(raw, offset);
            if (index == 0 && allowZero)
            {
                return "";
            }

            if (index < 1 || index > names.Count)
            {
                throw new SaveFormatException("Invalid player inventory name index.");
            }

            return names[index - 1];
        }
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
