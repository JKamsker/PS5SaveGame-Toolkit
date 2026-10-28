using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed record InventoryRecord(string Name, int Start, int DataOffset)
{
    public const int WordCount = 5;
    public const uint MaximumQuantity = 32_767;
    public bool IsAmmo => Name.StartsWith("Arrow", StringComparison.Ordinal) ||
        Name.StartsWith("Dart", StringComparison.Ordinal) || Name.StartsWith("Ammo", StringComparison.Ordinal);
    public bool IsResource => Name.StartsWith("Ingred", StringComparison.Ordinal) ||
        Name is "Supplies" or "Hide" or "Flowers" or "NGPFlowers";
    public string ValueId => $"inventory/{Name}/quantity";
    public string MirrorValueId => $"inventory/{Name}/quantity-copy";
    public int QuantityOffset => DataOffset + sizeof(uint);

    public bool CanEdit(ReadOnlySpan<byte> bytes)
    {
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[DataOffset..]);
        var quantity = BinaryPrimitives.ReadUInt32LittleEndian(bytes[QuantityOffset..]);
        return (IsAmmo || IsResource) && flags is 1 or 3 && quantity <= MaximumQuantity &&
            (!IsAmmo || quantity == BinaryPrimitives.ReadUInt32LittleEndian(bytes[(QuantityOffset + sizeof(uint))..]));
    }

    public void WriteQuantity(Span<byte> bytes, uint quantity)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[QuantityOffset..], quantity);
        if (IsAmmo)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[(QuantityOffset + sizeof(uint))..], quantity);
        }
    }
}

internal sealed record TsushimaInventory(int Start, int End, IReadOnlyList<InventoryRecord> Records)
{
    private const int RecordCount = 48;
    private const int SearchWindow = 512;
    private const int MaximumNameLength = 64;

    public static TsushimaInventory? Parse(ReadOnlySpan<byte> content)
    {
        // Save Wizard first searches for this length-prefixed equipment name, then searches forward.
        ReadOnlySpan<byte> anchor = "\x11\0\0\0LegendsInvestment"u8;
        var anchorOffset = content.IndexOf(anchor);
        if (anchorOffset < 0)
        {
            return null;
        }

        if (content[(anchorOffset + anchor.Length)..].IndexOf(anchor) >= 0)
        {
            throw new SaveFormatException("Tsushima inventory anchor is ambiguous.");
        }

        var searchStart = anchorOffset + anchor.Length;
        var search = content.Slice(searchStart, Math.Min(SearchWindow, content.Length - searchStart));
        ReadOnlySpan<byte> tableAnchor = "\x30\0\0\0\x0b\0\0\0ArrowNormal"u8;
        var relative = search.IndexOf(tableAnchor);
        if (relative < 0 || search[(relative + tableAnchor.Length)..].IndexOf(tableAnchor) >= 0)
        {
            throw new SaveFormatException("Tsushima counted inventory table is missing or ambiguous.");
        }

        var start = searchStart + relative;
        var position = start + sizeof(uint);
        var records = new List<InventoryRecord>(RecordCount);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < RecordCount; index++)
        {
            Require(content, position, sizeof(uint));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(content[position..]);
            if (length is 0 or > MaximumNameLength)
            {
                throw new SaveFormatException("Invalid Tsushima inventory name length.");
            }

            var recordStart = position;
            position += sizeof(uint);
            Require(content, position, (int)length + InventoryRecord.WordCount * sizeof(uint));
            var nameBytes = content.Slice(position, (int)length);
            foreach (var value in nameBytes)
            {
                if (!((value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z') ||
                      (value >= '0' && value <= '9') || value == '_'))
                {
                    throw new SaveFormatException("Invalid Tsushima inventory identifier.");
                }
            }

            var name = Encoding.ASCII.GetString(nameBytes);
            if (!names.Add(name))
            {
                throw new SaveFormatException("Duplicate Tsushima inventory identifier.");
            }

            position += (int)length;
            records.Add(new InventoryRecord(name, recordStart, position));
            position += InventoryRecord.WordCount * sizeof(uint);
        }

        if (records[^1].Name != "NGPFlowers" || !names.Contains("Supplies") || !names.Contains("Flowers"))
        {
            throw new SaveFormatException("Unrecognised Tsushima inventory table layout.");
        }

        return new TsushimaInventory(start, position, records);
    }

    private static void Require(ReadOnlySpan<byte> content, int position, int count)
    {
        if (position < 0 || count > content.Length - position)
        {
            throw new SaveFormatException("Tsushima inventory table is truncated.");
        }
    }
}
