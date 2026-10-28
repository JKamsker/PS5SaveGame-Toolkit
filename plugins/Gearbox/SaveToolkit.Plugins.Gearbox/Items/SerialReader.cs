// Altered C# adaptation of the zlib apocalyptech datalib.py layout reader; see SerialEnvelope.cs.
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed record SerialInfo
{
    public bool ChecksumValid { get; init; }
    public int Version { get; init; }
    public int? DatabaseVersion { get; init; }
    public int? Level { get; init; }
    public SerialAsset? Balance { get; init; }
    public SerialAsset? Inventory { get; init; }
    public SerialAsset? Manufacturer { get; init; }
    public IReadOnlyList<SerialAsset> Parts { get; init; } = [];
    public IReadOnlyList<SerialAsset> GenericParts { get; init; } = [];
    public int? Rerolls { get; init; }
    public int? Chaos { get; init; }
    public int RemainingBits { get; init; }
    public bool Complete { get; init; }
    public int? LevelOffset { get; init; }
    public int? RerollOffset { get; init; }
    public int? ChaosOffset { get; init; }
    public string Diagnostic { get; init; } = "Unknown serial.";
}

internal sealed class SerialReader(bool wonderlands)
{
    private const int HeaderMarker = 128;
    private const int ByteBits = 8;
    private const int VersionBits = 7;
    private const int LevelBits = 7;
    private const int PartCountBits = 6;
    private const int GenericCountBits = 4;
    private const int CustomizationCountBits = 4;
    private const int ChaosTierBits = 7;
    private const int MaximumPaddingBits = 7;
    private const int RerollSerialVersion = 4;
    private readonly SerialDatabase _database = SerialDatabase.For(wonderlands);

    public SerialInfo Read(ReadOnlySpan<byte> serial)
        => Read(serial, out _);

    internal SerialInfo Read(ReadOnlySpan<byte> serial, out SerialEnvelope? envelope)
    {
        envelope = null;
        var info = new SerialInfo { Version = serial.IsEmpty ? 0 : serial[0] };
        try
        {
            envelope = SerialEnvelope.Decode(serial, wonderlands);
            info = info with { ChecksumValid = true };
            var bits = new SerialBits(envelope.Payload);
            if (bits.Take(ByteBits) != HeaderMarker) throw new SaveFormatException("Unknown item header marker.");
            var version = bits.Take(VersionBits);
            info = info with { DatabaseVersion = version };
            if (version > _database.MaximumVersion)
                throw new SaveFormatException("Item database is newer than the pinned reference pack.");
            var balance = ReadAsset(bits, "InventoryBalanceData", version);
            var inventory = ReadAsset(bits, "InventoryData", version);
            var manufacturer = ReadAsset(bits, "ManufacturerData", version);
            var levelOffset = bits.Position;
            info = info with { Balance = balance, Inventory = inventory, Manufacturer = manufacturer, Level = bits.Take(LevelBits), LevelOffset = levelOffset };
            if (balance.Path is null || inventory.Path is null || manufacturer.Path is null)
                throw new SaveFormatException("Unmapped item header index; raw indices preserved.");
            var family = _database.Find(ItemCatalogs.Families, balance.Path.ToLowerInvariant());
            if (family is null) throw new SaveFormatException("Header decoded; part family unmapped. Tail preserved.");
            var parts = ReadAssets(bits, family.Name, version, PartCountBits);
            var generic = ReadAssets(bits, "InventoryGenericPartData", version, GenericCountBits);
            info = info with { Parts = parts, GenericParts = generic };
            var additional = bits.Take(ByteBits);
            for (var i = 0; i < additional; i++) _ = bits.Take(ByteBits);
            if (bits.Take(CustomizationCountBits) != 0) throw new SaveFormatException("Unknown customization layout; remaining bits preserved.");
            var rerollOffset = bits.Position;
            var rerolls = envelope.Version >= RerollSerialVersion ? bits.Take(ByteBits) : 0;
            var chaosOffset = bits.Position;
            int? chaos = wonderlands ? bits.Take(ChaosTierBits) : null;
            info = info with { Rerolls = rerolls, Chaos = chaos, RemainingBits = bits.Remaining };
            if (bits.Remaining > MaximumPaddingBits || bits.Take(bits.Remaining) != 0)
                throw new SaveFormatException("Unknown non-padding tail; original bytes preserved.");
            if (parts.Concat(generic).Any(p => p.Path is null))
                throw new SaveFormatException("Unmapped part index; original bytes preserved.");
            return info with { Complete = true, RerollOffset = envelope.Version >= RerollSerialVersion ? rerollOffset : null,
                ChaosOffset = wonderlands ? chaosOffset : null, Diagnostic = "CRC/header/parts verified against pinned layout; game legality unknown." };
        }
        catch (SaveFormatException ex) { return info with { Rerolls = null, Chaos = null, Diagnostic = ex.Message }; }
    }

    private SerialAsset ReadAsset(SerialBits bits, string category, int version)
    {
        var offset = bits.Position;
        var width = _database.Width(category, version);
        return _database.Asset(category, bits.Take(width)) with { Offset = offset, Width = width };
    }

    private SerialAsset[] ReadAssets(SerialBits bits, string category, int version, int countBits)
    {
        var count = bits.Take(countBits);
        var result = new SerialAsset[count];
        for (var i = 0; i < count; i++) result[i] = ReadAsset(bits, category, version);
        return result;
    }
}

internal sealed class SerialBits(ReadOnlyMemory<byte> bytes)
{
    private int _position;
    public int Position => _position;
    public int Remaining => bytes.Length * 8 - _position;
    public int Take(int count)
    {
        if (count is < 0 or > 31 || count > Remaining)
            throw new SaveFormatException("Truncated or unsupported item bit layout.");
        var value = 0;
        var data = bytes.Span;
        for (var i = 0; i < count; i++, _position++) value |= ((data[_position / 8] >> (_position % 8)) & 1) << i;
        return value;
    }
}
