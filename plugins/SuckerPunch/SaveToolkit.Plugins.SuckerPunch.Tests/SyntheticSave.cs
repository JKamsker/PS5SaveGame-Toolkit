using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

/// <summary>
/// Invented records in the layouts documented in README.md and YOTEI_FORMAT.md, not extracted saves.
/// Uses no production parser/writer/constants. Unknown fields and padding are deliberately nonzero.
/// </summary>
internal sealed class SyntheticSave
{
    internal const int HeaderSize = 16;
    internal const string FirstUnknown = "UnknownSlot05";
    internal const string SecondUnknown = "UnknownSlot06";
    internal const uint InitialQuantity = 2;
    internal const string YoteiStatisticsLast = "CrTriggerMarkedPerkEffects";
    internal const string YoteiGearLast = "SwordSwingFxOnryo";

    internal SyntheticSave(bool yotei, int prefixLength = 96, IReadOnlyList<string>? inventoryNames = null)
    {
        IsYotei = yotei;
        Bytes = new byte[yotei ? 616_448 : 555_008];
        using var stream = new MemoryStream(Bytes, writable: true);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = HeaderSize + prefixLength;
        if (yotei)
        {
            StatisticsStart = (int)stream.Position;
            Statistics = Table(writer, Names("FishSpeared", "Statistic", 33, YoteiStatisticsLast), sizeof(uint));
            GearStart = (int)stream.Position;
            Gear = Table(writer, Names("HalfShog101", "Gear", 1037, YoteiGearLast), 2);
        }
        else
        {
            AnchorStart = (int)stream.Position;
            WriteName(writer, "LegendsInvestment");
        }

        string[] names = inventoryNames?.ToArray() ?? ["ArrowNormal", yotei ? "Mon" : "Supplies", "Flowers", "ArrowFire", "TestItem",
            .. Enumerable.Range(5, 42).Select(index => $"UnknownSlot{index:D2}"),
            yotei ? "SurvivalCurrency" : "NGPFlowers"];
        InventoryStart = (int)stream.Position;
        Inventory = Table(writer, names, yotei ? 21 : 20);
        foreach (var data in Inventory.Values)
        {
            Write(data, 1);
            if (yotei) Bytes[data + sizeof(uint)] = 1;
            Write(data + QuantityRelativeOffset, InitialQuantity);
            Write(data + QuantityRelativeOffset + sizeof(uint), InitialQuantity);
        }
        if (yotei)
        {
            SecondaryStart = (int)stream.Position;
            Secondary = Table(writer, names, 16);
        }
        ContentEnd = (int)stream.Position;
        Write(0, yotei ? 0x150U : 0x14EU);
        Write(4, yotei ? 4U : (uint)ContentEnd);
        Write(8, yotei ? (uint)ContentEnd : 0);
        if (!yotei) Write(12, 49);
        Bytes[^1] = 0xA5;
        Repair();
    }

    internal bool IsYotei { get; }
    internal byte[] Bytes { get; }
    internal int AnchorStart { get; }
    internal int InventoryStart { get; }
    internal int SecondaryStart { get; }
    internal int StatisticsStart { get; }
    internal int GearStart { get; }
    internal int ContentEnd { get; }
    internal Dictionary<string, int> Inventory { get; }
    internal Dictionary<string, int> Secondary { get; } = [];
    internal Dictionary<string, int> Statistics { get; } = [];
    internal Dictionary<string, int> Gear { get; } = [];
    internal int QuantityRelativeOffset => IsYotei ? 5 : sizeof(uint);
    internal int ChecksumOffset => IsYotei ? 12 : 8;
    internal string ResourceName => IsYotei ? "Mon" : "Supplies";
    internal string Prefix => IsYotei ? "yotei/inventory/" : "inventory/";
    internal string ResourceId => Prefix + ResourceName + "/quantity";
    internal string ArrowId => Prefix + "ArrowNormal/quantity";
    internal string ActionId => Prefix + "set-quantity";
    internal string LastName => IsYotei ? "SurvivalCurrency" : "NGPFlowers";

    internal ISaveFile File() => SuckerPunchTestData.WithBytes(Bytes,
        IsYotei ? YoteiTestData.Title : SuckerPunchTestData.TsushimaTitle);
    internal ISaveDocument Open() => SuckerPunchTestData.Open(File());
    internal void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(offset), value);
    internal uint Checksum() => IsYotei ? YoteiTestData.Checksum(Bytes) : SuckerPunchTestData.Checksum(Bytes);
    internal void Repair() => Write(ChecksumOffset, Checksum());
    internal void SetEnd(int end) => Write(IsYotei ? 8 : 4, (uint)end);

    private static string[] Names(string first, string prefix, int count, string last) =>
        [first, .. Enumerable.Range(1, count - 2).Select(index => $"{prefix}{index:D4}"), last];

    private static Dictionary<string, int> Table(BinaryWriter writer, IEnumerable<string> names, int trailerLength)
    {
        var all = names.ToArray();
        writer.Write((uint)all.Length);
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in all)
        {
            WriteName(writer, name);
            offsets.Add(name, (int)writer.BaseStream.Position);
            writer.Write(Enumerable.Range(0, trailerLength).Select(index => (byte)(0x80 + index)).ToArray());
        }
        return offsets;
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        writer.Write((uint)name.Length);
        writer.Write(Encoding.ASCII.GetBytes(name));
    }
}
