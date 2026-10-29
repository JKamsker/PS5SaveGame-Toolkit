using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Resolves semantic data afresh; no query cursor, previous scope or plan is trusted.</summary>
internal sealed class PresetSelectors(Ff7Schema schema, GameplayFields gameplay, ReferenceData references,
    OwnedMateriaTargets owned)
{
    public IReadOnlyList<PresetTarget> Capture(string actionId, IReadOnlyList<string> rowKeys, bool allMatches, uint? materiaType)
    {
        var targets = new List<PresetTarget>();
        var ownedEntries = materiaType is { } typeId && !schema.IsRemake ? owned.Entries(typeId) : [];
        foreach (var key in rowKeys)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Selected rows must have current keys.");
            if (actionId == InventoryPlanning.ActionId &&
                ActionSlotKeys.TryParse(key, ActionSlotKeys.InventoryPrefix, GameplayFields.InventoryCount, out var slot))
            {
                var type = gameplay.TypeId(false, slot);
                if (gameplay.IsEmpty(false, slot)) throw new ArgumentException("Empty inventory rows have no semantic item target.");
                targets.Add(new(PresetIdentityDomain.ItemType, type, AllMatches: allMatches)
                    { Source = references.Source("ff7r-items", type) });
            }
            else if (IsMateria(actionId) &&
                ActionSlotKeys.TryParse(key, ActionSlotKeys.MateriaPrefix, GameplayFields.MateriaCount, out slot))
            {
                if (allMatches) throw new ArgumentException("Use all-copies scope for materia type targets.");
                if (schema.IsRemake) throw new ArgumentException("Remake selected materia has no proven owned-instance identity; use all-copies scope.");
                targets.Add(OwnedMateriaTargets.Capture(slot, ownedEntries));
            }
            else if (CharacterPlanning.Handles(actionId) &&
                ActionSlotKeys.TryParse(key, "character/", Ff7Schema.CharacterCount, out var character))
            {
                if (allMatches) throw new ArgumentException("Character targets use schema indexes, not all-matches.");
                targets.Add(new(PresetIdentityDomain.CharacterIndex, (uint)character)
                    { Source = references.CharacterSource(character) });
            }
            else throw new ArgumentException("Selected row is not in the action's current identity domain.");
        }
        return Array.AsReadOnly(targets.Distinct().ToArray());
    }

    public PresetResolution Resolve(Ff7ActionPreset preset, IReadOnlyDictionary<int, string>? choices = null)
    {
        Validate(preset);
        var ownedEntries = IsMateria(preset.ActionId) && !schema.IsRemake ? owned.Entries(ReadMateriaType(preset)) : [];
        if (preset.ScopeKind != PresetScopeKind.Selected)
        {
            if (choices is { Count: > 0 }) throw new ArgumentException("Only selected semantic targets accept choices.");
            if (!IsMateria(preset.ActionId) || schema.IsRemake)
                return new([], new()); // Current F6 planner determines eligibility and reports all skips.
            var type = ReadMateriaType(preset);
            var targets = ownedEntries.Where(entry => entry.TypeId == type)
                .Select(entry => new PresetTarget(PresetIdentityDomain.MateriaInstance, entry.InstanceId!.Value, type))
                .Distinct().ToArray();
            var resolutions = targets.Select(target => ResolveTarget(target, null, ownedEntries)).ToArray();
            if (resolutions.Length == 0)
                resolutions = [new(new(PresetIdentityDomain.MateriaType, type), PresetTargetStatus.Missing,
                    "missing-owned-copies", [], [])];
            return Result(resolutions);
        }

        if (choices is not null && choices.Keys.Any(index => index < 0 || index >= preset.Targets.Count))
            throw new ArgumentException("Choice refers to an unknown semantic target.");
        return Result(preset.Targets.Select((target, index) => ResolveTarget(target, choices?.GetValueOrDefault(index), ownedEntries)).ToArray());
    }

    public static bool IsMateria(string actionId) => actionId is MateriaPlanning.ActionId or MateriaPlanning.StarActionId;

    private PresetTargetResolution ResolveTarget(PresetTarget target, string? choice, IReadOnlyList<PresetCandidate> ownedEntries)
    {
        IReadOnlyList<PresetCandidate> candidates = target.Domain switch
        {
            PresetIdentityDomain.ItemType => Enumerable.Range(0, GameplayFields.InventoryCount)
                .Where(slot => !gameplay.IsEmpty(false, slot) && gameplay.TypeId(false, slot) == target.Id)
                .Select(slot => new PresetCandidate($"inventory/{slot}", slot, target.Id, null, PresetDetailStatus.Unique,
                    references.Source("ff7r-items", target.Id))).ToArray(),
            PresetIdentityDomain.MateriaInstance => ownedEntries.Where(entry => entry.InstanceId == target.Id && entry.TypeId == target.TypeId).ToArray(),
            PresetIdentityDomain.CharacterIndex => [new($"character/{target.Id}", (int)target.Id, 0, null,
                PresetDetailStatus.Unique, references.CharacterSource((int)target.Id))],
            _ => throw new ArgumentException("Foreign semantic identity domain."),
        };
        if (candidates.Count == 0)
        {
            if (choice is not null) throw new ArgumentException("Choice is not a current candidate.");
            return new(target, PresetTargetStatus.Missing, "missing-target", candidates, []);
        }
        if (candidates.Any(candidate => !candidate.Resolved))
        {
            if (choice is not null) throw new ArgumentException("An unresolved owned join cannot be chosen.");
            return new(target, PresetTargetStatus.Unresolved, candidates.First(candidate => !candidate.Resolved).Reason, candidates, []);
        }
        if (choice is not null)
        {
            if (target.AllMatches || candidates.Count(candidate => candidate.RowKey == choice) != 1)
                throw new ArgumentException("Choice is not a unique current candidate or conflicts with all-matches.");
            return new(target, PresetTargetStatus.Matched, "explicit-choice", candidates, [choice]);
        }
        return candidates.Count > 1 && !target.AllMatches
            ? new(target, PresetTargetStatus.Ambiguous, "choose-current-match", candidates, [])
            : new(target, PresetTargetStatus.Matched, target.AllMatches ? "explicit-all-matches" : "unique-match",
                candidates, candidates.Select(candidate => candidate.RowKey).ToArray());
    }

    private void Validate(Ff7ActionPreset preset)
    {
        if (!Enum.IsDefined(preset.ScopeKind)) throw new ArgumentException("Unknown semantic scope.");
        if (preset.ScopeKind == PresetScopeKind.AllCopies && !IsMateria(preset.ActionId) ||
            preset.ScopeKind == PresetScopeKind.AllEligible && IsMateria(preset.ActionId))
            throw new ArgumentException("Semantic scope is not supported by this action.");
        if (preset.ScopeKind != PresetScopeKind.Selected && preset.Targets.Count != 0)
            throw new ArgumentException("Only selected scope can carry explicit targets.");
        foreach (var target in preset.Targets)
        {
            if (target is null) throw new ArgumentException("Semantic targets cannot be null.");
            var valid = target.Domain switch
            {
                PresetIdentityDomain.ItemType => preset.ActionId == InventoryPlanning.ActionId &&
                    target.TypeId is null && !EmptySlots.IsEmptyType(target.Id, false, schema),
                PresetIdentityDomain.MateriaInstance => IsMateria(preset.ActionId) && !schema.IsRemake &&
                    target.Id != uint.MaxValue && target.TypeId is { } type &&
                    type == ReadMateriaType(preset) && !EmptySlots.IsEmptyType(type, true, schema) && !target.AllMatches,
                PresetIdentityDomain.CharacterIndex => CharacterPlanning.Handles(preset.ActionId) &&
                    target.Id < Ff7Schema.CharacterCount && target.TypeId is null && !target.AllMatches,
                _ => false,
            };
            if (!valid) throw new ArgumentException("Malformed or foreign semantic identity domain.");
        }
        if (preset.Targets.DistinctBy(target => (target.Domain, target.Id, target.TypeId)).Count() != preset.Targets.Count)
            throw new ArgumentException("Duplicate semantic targets must be captured once.");
    }

    private static uint ReadMateriaType(Ff7ActionPreset preset) => uint.Parse(preset.Parameters["type"].AsString(),
        System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);

    private static PresetResolution Result(IReadOnlyList<PresetTargetResolution> targets) =>
        new(targets, new(targets.SelectMany(target => target.RowKeys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
}
