using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record GarageVehicle(ulong Record, string List);

/// <summary>Walk entry lengths, then decode the garage's known hashed properties. Never scan for byte signatures.</summary>
internal static class GarageReader
{
    public const string Source = "Persistency wire-format research, WolvenKit 65ea74b (GPL-3.0; independently implemented): " +
        "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/PersistencySystem2Parser.cs; " +
        ScriptableReader.Source;
    private const int MaximumEntries = 100_000;
    private static readonly string[] GarageLists = ["activeVehicles", "unlockedVehicles", "uiFavoritedVehicles", "unlockedVehicleArray"];
    public static IReadOnlyList<GarageVehicle> Read(CsavContainer container, CsavTree tree)
    {
        if (!InventoryReader.IsVerifiedVersion(container)) throw new SaveFormatException("Garage schema version is unverified.");
        var nodes = tree.Nodes.Where(n => n.Name == "PersistencySystem2").ToArray();
        if (nodes.Length != 1) throw new SaveFormatException("No unique persistency node.");
        var raw = BinaryData.Slice(container.Payload, nodes[0].Offset, nodes[0].Size);
        var count = Count(raw, 4);
        var position = 8 + count * 4;
        BinaryData.Slice(raw, position, 8); // Unknown uint32 followed by entry count.
        var entries = Count(raw, position + 4);
        position += 8;
        byte[]? garage = null;
        for (var i = 0; i < entries; i++)
        {
            var id = BinaryData.UInt64(raw, position);
            position += 8;
            if (id == 0) continue;
            var type = BinaryData.UInt64(raw, position);
            var length = BinaryData.Int32(raw, position + 8);
            position += 12;
            var body = BinaryData.Slice(raw, position, length);
            if (type == PlayerStatsReader.NameHash("vehicleGarageComponentPS"))
            {
                if (garage is not null) throw new SaveFormatException("Ambiguous garage persistency entries.");
                garage = body.ToArray();
            }
            position += length;
        }
        if (position != raw.Length) throw new SaveFormatException("Unexplained persistency node suffix.");
        return garage is null ? throw new SaveFormatException("Garage persistency entry is absent.") : ReadBody(garage);
    }

    internal static IReadOnlyList<GarageVehicle> ReadBody(ReadOnlySpan<byte> body)
    {
        var result = new List<GarageVehicle>();
        var position = 0;
        var seen = new HashSet<ulong>();
        while (position < body.Length)
        {
            var name = BinaryData.UInt64(body, position);
            position += 8;
            if (name == 0)
            {
                if (position != body.Length) throw new SaveFormatException("Unexpected garage class terminator.");
                break;
            }
            if (!seen.Add(name)) throw new SaveFormatException("Duplicate garage property.");
            var type = BinaryData.UInt64(body, position);
            position += 8;
            var list = GarageLists
                .SingleOrDefault(candidate => PlayerStatsReader.NameHash(candidate) == name);
            if (list is null) throw new SaveFormatException("Garage contains an unverified property; ownership status unavailable. Raw bytes retained.");
            var expected = list switch
            {
                "activeVehicles" => "static:3,vehicleGarageVehicleID",
                "unlockedVehicleArray" => "array:vehicleUnlockedVehicle",
                _ => "array:vehicleGarageVehicleID",
            };
            if (type != PlayerStatsReader.NameHash(expected)) throw new SaveFormatException("Garage property type hash mismatch.");
            var count = Count(body, position);
            position += 4;
            if (list == "activeVehicles" && count != 3) throw new SaveFormatException("Unverified active vehicle slot count.");
            for (var i = 0; i < count; i++)
            {
                var record = Vehicle(body, ref position, list == "unlockedVehicleArray", 0);
                if (record != 0) result.Add(new GarageVehicle(record, list));
            }
        }
        return result;
    }

    private static ulong Vehicle(ReadOnlySpan<byte> body, ref int position, bool wrapper, int depth)
    {
        if (depth > 1) throw new SaveFormatException("Garage nesting exceeds its schema.");
        var seen = new HashSet<ulong>();
        ulong record = 0;
        while (true)
        {
            var name = BinaryData.UInt64(body, position);
            position += 8;
            if (name == 0) return record;
            if (!seen.Add(name)) throw new SaveFormatException("Duplicate garage vehicle property.");
            var type = BinaryData.UInt64(body, position);
            position += 8;
            if (wrapper && name == PlayerStatsReader.NameHash("vehicleID") && type == PlayerStatsReader.NameHash("vehicleGarageVehicleID"))
                record = Vehicle(body, ref position, false, depth + 1);
            else if (!wrapper && name == PlayerStatsReader.NameHash("recordID") && type == PlayerStatsReader.NameHash("TweakDBID"))
            {
                record = BinaryData.UInt64(body, position);
                position += 8;
            }
            else if (!wrapper && name == PlayerStatsReader.NameHash("name") && type == PlayerStatsReader.NameHash("CName"))
                position += BinaryData.Slice(body, position, 8).Length; // Preserve hashed name; never substitute it for ownership ID.
            else if (wrapper && name == PlayerStatsReader.NameHash("health") && type == PlayerStatsReader.NameHash("Float"))
                position += BinaryData.Slice(body, position, 4).Length;
            else throw new SaveFormatException("Unverified nested garage vehicle property.");
        }
    }

    private static int Count(ReadOnlySpan<byte> bytes, int position)
    {
        var count = BinaryData.Int32(bytes, position);
        return count is >= 0 and <= MaximumEntries ? count : throw new SaveFormatException("Invalid persistency entry count.");
    }
}
