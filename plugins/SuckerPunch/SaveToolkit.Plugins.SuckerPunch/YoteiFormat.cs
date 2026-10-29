using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Bounded Yōtei structures observed across the supplied PPSA26344 slots. No object relocation.</summary>
internal static class YoteiFormat
{
    public const int HeaderSize = 16;
    public const int ChecksumOffset = 12;
    public const int InventoryCount = 48;
    public const int InventoryTrailer = 21;
    public const int RegistryTrailer = 16;
    public const int StatisticsCount = 33;
    public const int GearCount = 1037;
    public const string Warning = "Not validated on console. Quantities and eligibility are inferred from named records across " +
        "11 non-empty samples. Bounds are observed values, not carrying capacities. Checksum coverage beyond the declared end " +
        "is unknown; the game may reject the edited save. Sibling files and menu metadata remain unchanged.";

    public static uint Word(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    public static void Write(Span<byte> bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);

    public static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        var end = Word(bytes, 8);
        if (end < HeaderSize || end > bytes.Length) throw new SaveFormatException("Invalid Yōtei declared content end.");
        ulong sum = SaveContainer.YoteiMagic + end;
        foreach (var value in bytes[HeaderSize..(int)end]) sum += value;
        return unchecked((uint)sum);
    }

    public static IReadOnlyList<YoteiRecord> Table(ReadOnlySpan<byte> content, int start, int count, int trailer)
    {
        var records = new List<YoteiRecord>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var position = start + sizeof(uint);
        for (var index = 0; index < count; index++)
        {
            Require(content, position, sizeof(uint));
            var length = Word(content, position);
            if (length is 0 or > 64) throw new SaveFormatException("Invalid Yōtei record name length.");
            position += sizeof(uint);
            Require(content, position, (int)length + trailer);
            var nameBytes = content.Slice(position, (int)length);
            if (!AsciiIdentifier.IsValid(nameBytes)) throw new SaveFormatException("Invalid Yōtei record identifier.");
            var name = Encoding.ASCII.GetString(nameBytes);
            if (!seen.Add(name)) throw new SaveFormatException("Duplicate Yōtei record identifier.");
            position += (int)length;
            records.Add(new YoteiRecord(name, position));
            position += trailer;
        }
        return records;
    }

    public static int FindUnique(ReadOnlySpan<byte> content, ReadOnlySpan<byte> anchor, string label)
    {
        var start = content.IndexOf(anchor);
        if (start < 0 || content[(start + anchor.Length)..].IndexOf(anchor) >= 0)
            throw new SaveFormatException($"Yōtei {label} is missing or ambiguous.");
        return start;
    }

    private static void Require(ReadOnlySpan<byte> content, int position, int length)
    {
        if (position < 0 || position > content.Length - length) throw new SaveFormatException("Yōtei named table is truncated.");
    }
}

internal sealed record YoteiRecord(string Name, int DataOffset)
{
    public string QuantityId => $"yotei/inventory/{Name}/quantity";
    public string CopyId => $"yotei/inventory/{Name}/quantity-copy";
    public int QuantityOffset => DataOffset + sizeof(uint) + sizeof(byte);
    public bool Ammo => Name.StartsWith("Arrow", StringComparison.Ordinal) || Name.StartsWith("Ammo", StringComparison.Ordinal);
    public uint Quantity(ReadOnlySpan<byte> bytes) => YoteiFormat.Word(bytes, QuantityOffset);

    // Bounds are maxima across all supplied generations, not claimed game capacities.
    public uint? ObservedMaximum => Name switch
    {
        "Mon" => 112_099, "Flowers" => 22, "IngredComWood" => 95_099, "IngredComCloth" => 52,
        "IngredComMetal" => 13, "IngredRarMetal" => 10, "IngredUncPowder" => 50,
        "IngredComFood" => 1, "IngredComPlant" => 3, "ArrowNormal" => 9, "ArrowFire" => 3,
        "ArrowHeavy" => 8, "ArrowDisarm" => 4, "AmmoSake" => 1, _ => null,
    };

    public bool Eligible(ReadOnlySpan<byte> bytes) => ObservedMaximum.HasValue &&
        YoteiFormat.Word(bytes, DataOffset) == 1 && bytes[DataOffset + sizeof(uint)] == 1 &&
        (!Ammo || Quantity(bytes) == YoteiFormat.Word(bytes, QuantityOffset + sizeof(uint)));

    public void Write(Span<byte> bytes, uint quantity)
    {
        YoteiFormat.Write(bytes, QuantityOffset, quantity);
        if (Ammo) YoteiFormat.Write(bytes, QuantityOffset + sizeof(uint), quantity);
    }
}
