using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Discovers and rebinds the complete XP consequence set without widening cold work.</summary>
internal sealed class GearboxProgressionBindings(GearboxDocument document)
{
    private ProtoMessage? _root;
    private ProtoMessage[] _stats = [];
    private int[] _ordinals = [];

    internal static string RecordedLevelId(int index) => index == 0 ? "character/recorded-level" : $"character/recorded-level/{index}";

    internal bool Ensure(bool boundedDiscovery)
    {
        if (document.Profile) return false;
        var hasExperience = GearboxMetadataLookup.HasField(document.Root, "experience_points", out var rootExhausted);
        if (!hasExperience) return !rootExhausted;
        if (boundedDiscovery && rootExhausted) return false;

        if (!GearboxMetadataLookup.TryChildAt(document.Root, "ability_data", 0, out var ability, out var abilityExhausted) &&
            boundedDiscovery && abilityExhausted)
            return false;
        if (!GearboxMetadataLookup.TryChildrenWhere(document.Root, "game_stats_data",
            stat => stat.Text("stat_path") == CharacterProgression.LevelStat(document.Root.Schema.Wonderlands),
            out var stats, out var statsExhausted) && boundedDiscovery && statsExhausted)
            return false;

        _ = document.Level();
        _ = document.Experience();
        if (ability is not null)
        {
            // Proto3 integer fields have a logical zero default. CharacterProgression writes these
            // same canonical fields when levelling, including when their physical tags are absent.
            _ = document.Integer("character/skill-points", "Unspent skill points",
                () => GearboxMetadataLookup.RequireChild(document.Root, "ability_data", 0, boundedDiscovery), "ability_points",
                warning: "Spent points are preserved; levelling down does not refund them.");
            _ = document.Integer("character/tree-grade", "Skill tree grade",
                () => GearboxMetadataLookup.RequireChild(document.Root, "ability_data", 0, boundedDiscovery), "tree_grade",
                warning: "Advanced progression value; levelling above level one initializes the canonical default.");
        }

        _root = document.Root;
        _stats = stats.Select(pair => pair.Message).ToArray();
        _ordinals = stats.Select(pair => pair.Index).ToArray();
        for (var i = 0; i < stats.Count; i++)
        {
            var statIndex = i;
            var id = RecordedLevelId(statIndex);
            _ = document.Integer(id, "Recorded level statistic", () => Get(statIndex), "stat_value", int.MaxValue,
                warning: "Updated with XP; this statistic alone does not change level.");
        }
        return true;
    }

    private ProtoMessage Get(int index)
    {
        if (!ReferenceEquals(_root, document.Root))
        {
            var allStats = document.Root.All("game_stats_data").ToArray();
            _stats = _ordinals.Where(ordinal => ordinal >= 0 && ordinal < allStats.Length)
                .Select(ordinal => allStats[ordinal].Message!).ToArray();
            _root = document.Root;
        }
        return index >= 0 && index < _stats.Length
            ? _stats[index]
            : throw new SaveFormatException("The recorded level statistic is no longer present.");
    }

    internal void Invalidate()
    {
        _root = null;
        _stats = [];
        _ordinals = [];
    }
}
