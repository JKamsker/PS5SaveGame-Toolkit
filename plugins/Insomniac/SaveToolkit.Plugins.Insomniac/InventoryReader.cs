using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed record InventoryRecord(uint Type, int Offset, int Size, string Key,
    string? GuidBytes, string? ConfigBytes, uint? Count, uint? Xp, float? CheckpointAmmo, float? LoadedAmmo);

internal sealed record InventoryBlock(uint Tag, int Occurrence, int Offset, int Size, int OpaqueBytes,
    IReadOnlyList<InventoryRecord> Records)
{
    internal string Key => $"{Tag:x8}/{Occurrence}";
}

/// <summary>Independent PS5 framing established across both supplied campaigns.
/// No scanning/splitting at TVNI, no assumed actor/ownership, and no write support.</summary>
internal static class InventoryReader
{
    internal const uint SectionTag = 0x00D0_A9DF;
    internal const uint Magic = 0x494E_5654;
    private const int MaxBlocks = 256;
    private const int MaxRecords = 4096;
    private const uint WeaponType = 0xA89E_9C3C;
    private const uint MeleeType = 0x2F1E_071F;
    private const uint GenericType = 0x4E5C_E3CB;
    private const uint SwingshotType = 0x388B_19BC;
    private const uint VanityType = 0xF6AD_1389;
    private const int GenericPayloadSize = 23;
    private const int WeaponPayloadSize = 119;
    private const int VanityPayloadSize = 460;
    private const int WeaponXpOffset = 24;
    private const int WeaponCheckpointAmmoOffset = 28;
    private const int WeaponLoadedAmmoOffset = 32;

    internal static IReadOnlyList<InventoryBlock> Read(Dat1Container container)
    {
        var sections = container.Sections.Where(s => s.Tag == SectionTag).ToArray();
        if (sections.Length > 1) throw Invalid("Duplicate inventory section.");
        return sections.Length == 0 ? [] : Read(container, sections[0]);
    }

    internal static IReadOnlyList<InventoryBlock> Read(Dat1Container container, Dat1Section section)
    {
        var bytes = container.Bytes;
        var end = checked((int)section.End);
        var position = checked((int)section.Offset);
        var blocks = new List<InventoryBlock>();
        var occurrences = new Dictionary<uint, int>();
        var totalRecords = 0;
        while (position < end)
        {
            if (blocks.Count >= MaxBlocks) throw Invalid("Block budget exceeded.");
            var start = position;
            var tag = Word(bytes, ref position, end);
            var size = Word(bytes, ref position, end);
            if (size > end - position) throw Invalid("Block extends past section.");
            var blockEnd = checked(position + (int)size);
            if (Word(bytes, ref position, blockEnd) != Magic) throw Invalid("Expected bounded TVNI block.");
            var count = Word(bytes, ref position, blockEnd);
            if (count > MaxRecords - totalRecords) throw Invalid("Record budget exceeded.");
            totalRecords += checked((int)count);
            var records = new List<InventoryRecord>();
            var identities = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var type = Word(bytes, ref position, blockEnd);
                var length = Word(bytes, ref position, blockEnd);
                if (length > blockEnd - position) throw Invalid("Record extends past block.");
                var payload = bytes.AsSpan(position, checked((int)length));
                var record = Decode(type, payload, position);
                var key = record.GuidBytes ?? $"opaque-{index}";
                var occurrence = identities.GetValueOrDefault(key);
                identities[key] = occurrence + 1;
                records.Add(record with { Key = $"{key}/{occurrence}" });
                position += checked((int)length);
            }
            var opaque = blockEnd - position;
            position = blockEnd;
            // These two container types have a separate four-byte suffix outside
            // their declared payload. Verified at every boundary in all 36 saves.
            if (tag is 0x3747_5015 or 0x4BF3_942C)
            {
                if (Word(bytes, ref position, end) != 0) throw Invalid("Unsupported external block suffix.");
                opaque += sizeof(uint);
            }
            var blockOccurrence = occurrences.GetValueOrDefault(tag);
            occurrences[tag] = blockOccurrence + 1;
            blocks.Add(new InventoryBlock(tag, blockOccurrence, start, position - start, opaque, records));
        }
        return blocks;
    }

    private static InventoryRecord Decode(uint type, ReadOnlySpan<byte> payload, int offset)
    {
        var maskBytes = type switch
        {
            GenericType or SwingshotType when payload.Length == GenericPayloadSize && payload[0] == 0x0F => 1,
            WeaponType or MeleeType when payload.Length == WeaponPayloadSize && payload[0] == 0xFF && payload[1] == 0x3F => 2,
            VanityType when payload.Length == VanityPayloadSize && payload[0] == 0xFF && payload[1] == 0x1F => 2,
            _ => 0,
        };
        if (maskBytes == 0) return new(type, offset, payload.Length, "", null, null, null, null, null, null);
        var guid = Convert.ToHexStringLower(payload.Slice(maskBytes, sizeof(ulong)));
        var config = Convert.ToHexStringLower(payload.Slice(maskBytes + sizeof(ulong), sizeof(ulong)));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload[(maskBytes + 2 * sizeof(ulong))..]);
        uint? xp = null;
        float? checkpoint = null;
        float? loaded = null;
        if (type is WeaponType or MeleeType)
        {
            // Unlike the LE container and item count, these candidate schema slots
            // use BE in the measured fixed-mask payload. HUD semantics remain inferred.
            xp = BinaryPrimitives.ReadUInt32BigEndian(payload[WeaponXpOffset..]);
            checkpoint = BinaryPrimitives.ReadSingleBigEndian(payload[WeaponCheckpointAmmoOffset..]);
            loaded = BinaryPrimitives.ReadSingleBigEndian(payload[WeaponLoadedAmmoOffset..]);
            if (!float.IsFinite(checkpoint.Value) || !float.IsFinite(loaded.Value))
            {
                // Interpretation is inferred: an unsupported state must remain
                // inspectable rather than being rejected as corrupt gameplay.
                xp = null;
                checkpoint = null;
                loaded = null;
            }
        }
        return new(type, offset, payload.Length, "", guid, config, count, xp, checkpoint, loaded);
    }

    private static uint Word(byte[] bytes, ref int position, int limit)
    {
        if (limit - position < sizeof(uint)) throw Invalid("Truncated header or suffix.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position));
        position += sizeof(uint);
        return value;
    }

    private static SaveFormatException Invalid(string message) => new($"Inventory section: {message}");
}
