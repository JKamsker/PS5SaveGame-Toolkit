using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record AbilityEntry(string Id, TaggedRecord Record);
internal sealed record AbilityArray(string Name, IReadOnlyList<AbilityEntry> Entries);

/// <summary>Read-only projections of exact, bounded tagged arrays; unknown properties remain in each record.</summary>
internal static class AbilityReader
{
    private const int HandleHeaderSize = 8;
    private const int MaximumArrayRecords = 4096;
    private static IReadOnlyList<(string Name, string Type, string Key)> Arrays => [
        ("skills", "SSkill", "skillType"), ("skillSlots", "SSkillSlot", "id"),
        ("mutagenSlots", "SMutagenSlot", "equipmentSlot"), ("mutations", "SMutation", "type"),
        ("statPoints", "SBaseStat", "type"), ("resistStats", "SResistanceValue", "type"),
    ];

    public static IReadOnlyList<AbilityArray> Read(Sav3Node node, byte[] data, IReadOnlyList<string> names)
    {
        var raw = BinaryData.Slice(data, node.ValueOffset, node.ValueSize);
        if (raw.Length < HandleHeaderSize || raw[0] != 0 || raw[1] != 1 ||
            Name(BinaryData.UInt16(raw, 6), names) != "W3PlayerAbilityManager")
        {
            throw new SaveFormatException("Unsupported ability manager handle/class.");
        }

        var position = node.ValueOffset + HandleHeaderSize;
        var end = node.ValueOffset + node.ValueSize;
        var manager = TaggedRecord.Read(data, ref position, end, names);
        if (position != end) { throw new SaveFormatException("Trailing ability manager bytes."); }
        var result = new List<AbilityArray>();
        foreach (var (name, type, key) in Arrays)
        {
            if (manager.Find(name) is not { } property) { continue; }
            if (property.Type != $"array:2,0,{type}") { throw new SaveFormatException($"Unsupported {name} array type."); }
            var count = BinaryData.Int32(property.Value, 0);
            if (count is < 0 or > MaximumArrayRecords || count > (property.Value.Length - sizeof(int)) / 3)
            {
                throw new SaveFormatException($"Invalid {name} array count.");
            }

            position = property.Offset + 12;
            end = property.Offset + property.Bytes.Length;
            var entries = new List<AbilityEntry>(count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var record = TaggedRecord.Read(data, ref position, end, names);
                var identity = record.Find(key);
                // Empty definition slots have no native identity; retain their slot, without inventing a skill.
                var id = identity is null ? $"unnamed-slot-{i}" : key == "id"
                    ? record.Integer(key).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : NameValue(identity, names);
                if (!ids.Add(id)) { throw new SaveFormatException($"Ambiguous {name} record identity '{id}'."); }
                entries.Add(new AbilityEntry(id, record));
            }

            if (position != end) { throw new SaveFormatException($"Trailing {name} array bytes."); }
            result.Add(new AbilityArray(name, entries));
        }

        return result;
    }

    public static string NameValue(TaggedProperty property, IReadOnlyList<string> names)
    {
        if (property.Value.Length != sizeof(ushort)) { throw new SaveFormatException("Invalid saved enum/name length."); }
        return Name(BinaryPrimitives.ReadUInt16LittleEndian(property.Value), names);
    }

    private static string Name(int index, IReadOnlyList<string> names) => index > 0 && index <= names.Count
        ? names[index - 1] : throw new SaveFormatException("Invalid saved ability name index.");
}
