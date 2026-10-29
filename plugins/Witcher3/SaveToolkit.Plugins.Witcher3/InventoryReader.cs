using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Reads one bounded native component; offsets exposed by items address the original payload.</summary>
internal sealed class InventoryReader(byte[] data, IReadOnlyList<string> names, int payloadOffset)
{
    private const uint EquippedFlag = 0x200000;
    private const int MaximumItems = 10_000;
    private const int ClassOffset = 7;
    private const int ComponentHeaderSize = 6;
    private const int RecordPrefixSize = 24;
    private const int QuantityPosition = 17;
    private const int AttributeSize = 7;
    private readonly HashSet<int> _ids = [];
    private int _position = ClassOffset + ComponentHeaderSize;
    private ushort _nextId;

    public PlayerInventory Read()
    {
        Need(ClassOffset, ComponentHeaderSize);
        if (Name(ClassOffset) != "CInventoryComponent")
        {
            throw new SaveFormatException("Invalid player inventory class index.");
        }

        _nextId = BinaryData.UInt16(data, ClassOffset + 2);
        var count = BinaryData.UInt16(data, ClassOffset + 4);
        if (count > MaximumItems || count > _nextId)
        {
            throw new SaveFormatException("Invalid player inventory item count.");
        }

        var items = new List<InventoryItem>(count);
        for (var index = 0; index < count; index++) { items.Add(ReadItem()); }

        ReadOnlySpan<byte> terminal = [1, 0, 0, 0, 0, 0];
        if (!data.AsSpan(_position).SequenceEqual(terminal))
        {
            throw new SaveFormatException("Unsupported player inventory terminal or trailing data.");
        }

        return new PlayerInventory(items);
    }

    private InventoryItem ReadItem()
    {
        var start = _position;
        Need(_position, RecordPrefixSize);
        var name = Name(_position);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_position + 2));
        string[] prefixNames = [Name(_position + 13, allowZero: true), Name(_position + 15, allowZero: true)];
        var quantityOffset = _position + QuantityPosition;
        var quantity = BinaryData.UInt16(data, quantityOffset);
        var durability = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(_position + 19));
        var attributes = data[_position + 23];
        if (quantity == 0 || !float.IsFinite(durability) || attributes > 32)
        {
            throw new SaveFormatException("Invalid player inventory quantity, durability or attributes.");
        }

        _position += RecordPrefixSize;
        var itemAttributes = ReadAttributes(attributes);
        Need(_position, 4);
        var id = data[_position] | data[_position + 1] << 8 | data[_position + 2] << 16;
        var tags = data[_position + 3];
        if (id < 1 || id > _nextId || !_ids.Add(id) || tags > 128)
        {
            throw new SaveFormatException("Invalid player inventory item ID or tag count.");
        }

        _position += 4;
        var itemTags = new List<string>(tags);
        for (var tag = 0; tag < tags; tag++) { itemTags.Add(ReadName()); }
        var equipped = (flags & EquippedFlag) != 0;
        var slot = equipped ? ReadName() : "";
        return new InventoryItem(name, id, payloadOffset + quantityOffset, quantity, equipped) {
            Flags = flags, Durability = durability, OriginalDurability = durability, Slot = slot, Tags = itemTags, Attributes = itemAttributes,
            PrefixNames = prefixNames, OpaquePrefix = Convert.ToHexString(data.AsSpan(start + 6, 7)),
            Offset = payloadOffset + start, Size = _position - start,
            OpaqueHash = Convert.ToHexString(SHA256.HashData(data.AsSpan(start + 6, 7))),
        };
    }

    private List<InventoryAttribute> ReadAttributes(int count)
    {
        var attributes = new List<InventoryAttribute>(count);
        for (var index = 0; index < count; index++)
        {
            Need(_position, AttributeSize);
            attributes.Add(new InventoryAttribute(Name(_position), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_position + 2)), data[_position + 6]));
            _position += AttributeSize;
        }

        return attributes;
    }

    private string ReadName()
    {
        var name = Name(_position);
        _position += sizeof(ushort);
        return name;
    }

    private void Need(int offset, int size) => _ = BinaryData.Slice(data, offset, size);

    private string Name(int offset, bool allowZero = false)
    {
        var index = BinaryData.UInt16(data, offset);
        if (index == 0 && allowZero) { return ""; }
        if (index < 1 || index > names.Count)
        {
            throw new SaveFormatException("Invalid player inventory name index.");
        }

        return names[index - 1];
    }
}
