using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Composes only the existing qualified character bindings; never calculates derived stats.</summary>
internal sealed class CharacterPlanning(Ff7Schema schema, CharacterView characters, ReferenceData references)
{
    public const string RecoveryActionId = "character/recover-current-vitals";
    public const string LevelActionId = "character/set-level";
    public const string SelectionTableId = "characters";
    private const string CharacterPrefix = "character/";
    private const string RecoveryWarning = WriteWarnings.Experimental +
        "Inferred current HP/MP mappings. Only values below their opening stored maxima are raised. " +
        "Over-max values, maxima, XP and equipment are preserved; no status effects or derived stats are repaired. ";
    private const string LevelWarning = WriteWarnings.Progression +
        "Inferred character level targets set XP to the source tier threshold. Maxima, abilities, rewards and guest/past-character progression are not repaired. ";
    private readonly uint[][] _curves = ProgressionEdits.CharacterCurves(schema);

    public IReadOnlyList<ActionNode> Views() =>
    [
        new ActionNode(RecoveryActionId, "Recover selected characters' current HP/MP")
        {
            SupportsPlanning = true,
            SelectionTableId = SelectionTableId,
            Description = RecoveryWarning + "Preview selected rows, or all seven displayed records when no selection is supplied. " + EmbeddedCatalogs.Attribution,
        },
        new ActionNode(LevelActionId, "Set selected character levels")
        {
            SupportsPlanning = true,
            SelectionTableId = SelectionTableId,
            Description = LevelWarning + "Preview selected rows, or all seven displayed records when no selection is supplied. Invalid records and ambiguous source tiers are skipped.",
            Parameters = [new("level", "Target level", IntegerSpec.UInt8 with { Min = 1, Max = _curves.Max(curve => curve.Length) }, FieldValue.Of(25L))],
        },
    ];

    public static bool Handles(string actionId) => actionId is RecoveryActionId or LevelActionId;

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (scope.QueryScope is not null) return Error("scope", "Select displayed character rows; character query scopes are unsupported.");
        var recovery = actionId == RecoveryActionId;
        var target = 0;
        if (recovery)
        {
            if (parameters.Count != 0) return Error("parameters", "Recovery has no parameters.");
        }
        else
        {
            try
            {
                if (parameters.Count != 1 || !parameters.TryGetValue("level", out var value) ||
                    (target = value.AsInt32()) < 1 || target > _curves.Max(curve => curve.Length))
                    return Error("level", "Target level must be within the variant's source curves.");
            }
            catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
            {
                return Error("level", "Target level must be an integer within the variant's source curves.");
            }
        }

        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        var keys = scope.RowKeys ?? Enumerable.Range(0, Ff7Schema.CharacterCount).Select(index => $"{CharacterPrefix}{index}").ToArray();
        foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!ActionSlotKeys.TryParse(key, CharacterPrefix, Ff7Schema.CharacterCount, out var character))
            {
                skipped.Add(new(key, "Not a displayed, source-qualified character record; guest/past records are excluded."));
                continue;
            }
            if (!characters.Definition(character, "level").Editable)
            {
                skipped.Add(new(key, "Original level is outside the source curve; unsupported record stays locked."));
                continue;
            }

            var before = changes.Count;
            if (recovery)
            {
                Recover(character, "hp", changes);
                Recover(character, "mp", changes);
            }
            else
            {
                var curve = _curves[character];
                if (target > curve.Length || curve.Count(threshold => threshold <= curve[target - 1]) != target)
                {
                    skipped.Add(new(key, "Target tier is ambiguous or outside this character's source curve."));
                    continue;
                }
                if (characters.Value(character, "level") != target)
                {
                    Add(character, "level", (uint)target, changes);
                    Add(character, "experience", curve[target - 1], changes, derived: true);
                }
            }
            if (changes.Count == before) skipped.Add(new(key, recovery
                ? "Current HP/MP are already at or above their opening stored maxima; nothing is reduced."
                : "Already at the target level; the existing XP tuple is preserved."));
        }

        return new() { Changes = changes, SkippedRows = skipped, Warnings =
            [recovery ? RecoveryWarning + EmbeddedCatalogs.Attribution : LevelWarning,
                .. schema.IsDemo ? new[] { "PC retail field mapping is inferred for the PS5 demo; no in-game confirmation." } : []] };
    }

    private void Recover(int character, string suffix, List<PlannedChange> changes)
    {
        var field = characters.Definition(character, suffix);
        var maximum = characters.Value(character, "max-" + suffix);
        if (field.Editable && field.Spec is IntegerSpec range && maximum <= range.Max && characters.Value(character, suffix) < maximum)
            Add(character, suffix, maximum, changes);
    }

    private void Add(int character, string suffix, uint value, List<PlannedChange> changes, bool derived = false)
    {
        var old = characters.Value(character, suffix);
        // Keep XP in the replay tuple even when only the level changes. Without it,
        // undo derives XP from the old level and loses an inconsistent opening pair.
        if (old == value && !derived) return;
        var field = characters.Definition(character, suffix);
        changes.Add(new(field.Id, $"{references.Character(character)} · {field.Label}", FieldValue.Of((long)old), FieldValue.Of((long)value)) { Derived = derived });
    }

    private static ActionPlan Error(string field, string message) => new() { Errors = [new(field, message)] };
}
