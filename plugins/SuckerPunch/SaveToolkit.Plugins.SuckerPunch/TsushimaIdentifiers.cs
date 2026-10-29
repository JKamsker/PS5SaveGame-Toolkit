using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.SuckerPunch;

internal enum IdentifierGroup
{
    Equipment,
    Technique,
    Character,
}

internal sealed record TsushimaIdentifier(string Name, int Offset, IdentifierGroup Group);

internal static class TsushimaIdentifiers
{
    private const int MaximumNameLength = 64;
    private const int MaximumResults = 2_000;

    public static IReadOnlyList<TsushimaIdentifier> Scan(ReadOnlySpan<byte> content)
    {
        var results = new List<TsushimaIdentifier>();
        var seen = new HashSet<(string, IdentifierGroup)>();
        for (var offset = sizeof(uint); offset <= content.Length - sizeof(uint); offset++)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(content[(offset - sizeof(uint))..]);
            if (length is 0 or > MaximumNameLength || length > content.Length - offset)
            {
                continue;
            }

            var bytes = content.Slice(offset, (int)length);
            if (!AsciiIdentifier.IsValid(bytes))
            {
                continue;
            }

            var name = Encoding.ASCII.GetString(bytes);
            if (!TryClassify(name, out var group) || !seen.Add((name, group)))
            {
                continue;
            }

            results.Add(new TsushimaIdentifier(name, offset - sizeof(uint), group));
            if (results.Count == MaximumResults)
            {
                break;
            }
        }

        return results;
    }

    private static bool TryClassify(string name, out IdentifierGroup group)
    {
        if (name.StartsWith("Charmed", StringComparison.Ordinal))
        {
            group = IdentifierGroup.Character;
            return true;
        }

        if (name.StartsWith("Armor", StringComparison.Ordinal) ||
            name.StartsWith("Sword", StringComparison.Ordinal) ||
            name.StartsWith("Weapon", StringComparison.Ordinal) ||
            name.StartsWith("Charm", StringComparison.Ordinal) ||
            name.StartsWith("Acc", StringComparison.Ordinal) ||
            name.StartsWith("Head", StringComparison.Ordinal) ||
            name is "HorseArmor" or "SuperArmor" or "RoleAbilityVampsword")
        {
            group = IdentifierGroup.Equipment;
            return true;
        }

        if (name.EndsWith("Stance", StringComparison.Ordinal) ||
            name.StartsWith("Stance", StringComparison.Ordinal) ||
            name.Contains("Technique", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Ultimate", StringComparison.Ordinal))
        {
            group = IdentifierGroup.Technique;
            return true;
        }

        if (name.StartsWith("Cr", StringComparison.Ordinal) ||
            name.StartsWith("Skill", StringComparison.Ordinal) ||
            name.Contains("Point", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rank", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Legend", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Damage", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("EnemiesKilled", StringComparison.OrdinalIgnoreCase))
        {
            group = IdentifierGroup.Character;
            return true;
        }

        group = default;
        return false;
    }
}
