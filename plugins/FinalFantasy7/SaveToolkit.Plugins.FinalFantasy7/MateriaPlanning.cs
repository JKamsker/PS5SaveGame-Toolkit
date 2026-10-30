using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Plans AP edits and dependent stored-star transitions; ownership is preserved.</summary>
internal sealed class MateriaPlanning(Ff7Schema schema, GameplayFields gameplay, ReferenceData references)
{
    public const string ActionId = "materia/set-ap-within-tier";
    public const string StarActionId = "materia/set-stars";
    private const byte MaximumStarTier = 5;

    public ActionNode View(bool stars = false) => new(stars ? StarActionId : ActionId, stars ? "Set stars and dependent AP" : "Set AP and derived stars")
    {
        SupportsPlanning = true,
        SelectionTableId = "materia",
        Description = (stars ? "Choose a materia type and star tier; dependent AP is set to that tier threshold. " : "Choose a materia type, then preview AP changes. ") +
            "Scope covers selected rows or all owned copies. " +
            "Summons and mismatched rows are skipped. Ordinary stars follow the pinned AP curve. " + WriteWarnings.Experimental + " " + EmbeddedCatalogs.Attribution,
        Parameters =
        [
            new("type", "Materia type", new ReferenceSpec("ff7r-materia")
            {
                Assess = record => new ReferenceEligibility
                {
                    Eligible = uint.TryParse(record.Key, out var type) && IsTypeEligible(type),
                    Reasons = ["Only owned, ordinary materia with coherent AP and stored stars can be edited."],
                },
            }, FieldValue.Of(DefaultType())) { Description = EmbeddedCatalogs.Attribution },
            stars ? new("stars", "Target stars", IntegerSpec.UInt8 with { Min = 1, Max = MaximumStarTier }, FieldValue.Of(1L)) :
            new("ap", "Target AP", IntegerSpec.UInt32 with { Max = MateriaEditPolicy.MaximumTargetAp },
                FieldValue.Of((long)Enumerable.Range(0, GameplayFields.MateriaCount).Where(slot => gameplay.ApRange(slot) is not null)
                    .Select(gameplay.Ap).FirstOrDefault())),
        ],
    };

    public bool IsTypeEligible(uint type) => Enumerable.Range(0, GameplayFields.MateriaCount)
        .Any(slot => gameplay.TypeId(true, slot) == type && gameplay.ApRange(slot) is not null);

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId is not (ActionId or StarActionId))
        {
            return Error("action", "Unknown FFVII planned action.");
        }

        if (scope.QueryScope is { } query && (query.CollectionId != "materia" || scope.RowKeys is null))
        {
            return Error("scope", "Only a Core-resolved materia query scope can be used for AP planning.");
        }

        var stars = actionId == StarActionId;
        var error = ReadTarget(parameters, stars, out var type, out var ap);
        return error ?? PlanRows(type, ap, stars, scope);
    }

    private ActionPlan? ReadTarget(IReadOnlyDictionary<string, FieldValue> parameters, bool stars, out uint type, out uint ap)
    {
        type = 0;
        ap = 0;
        try
        {
            if (parameters.Count != 2 || !parameters.TryGetValue("type", out var typeValue) ||
                !uint.TryParse(typeValue.AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out type) ||
                !parameters.TryGetValue(stars ? "stars" : "ap", out var apValue) || (ap = apValue.AsUInt32()) > MateriaEditPolicy.MaximumTargetAp ||
                !EmbeddedCatalogs.Records(schema, "ff7r-materia").Any(record => record.Key == typeValue.AsString()))
            {
                return Error("parameters", stars ? "Choose a known materia type and its supported star tier."
                    : "Choose a known materia type and AP between 0 and 999,999.");
            }
            if (stars)
            {
                var curve = EmbeddedCatalogs.Records(schema, "ff7r-materia").Single(record => record.Key == typeValue.AsString())
                    .Attributes["apThresholds"].Split(',').Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                if (ap < 1 || ap > curve.Length) return Error("stars", "Choose a star tier supported by this materia type.");
                ap = curve[ap - 1];
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return Error("parameters", "Invalid materia type or AP value.");
        }

        return null;
    }

    private ActionPlan PlanRows(uint type, uint ap, bool stars, ActionScope scope)
    {
        var keys = scope.RowKeys ?? Enumerable.Range(0, GameplayFields.MateriaCount)
            .Where(slot => gameplay.TypeId(true, slot) == type).Select(slot => $"materia/{slot}").ToArray();
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!ActionSlotKeys.TryParse(key, ActionSlotKeys.MateriaPrefix, GameplayFields.MateriaCount, out var slot))
            {
                skipped.Add(new(key, "Not a current materia slot."));
                continue;
            }

            var range = gameplay.ApRange(slot);
            var reason = gameplay.TypeId(true, slot) != type ? "Different materia type."
                : range is null ? "Empty, unknown, summon or AP/level mismatch."
                : ap < range.Min || ap > range.Max ? "Target AP is outside the editor range."
                : gameplay.Ap(slot) == ap ? "Already has the requested AP." : null;
            if (reason is not null)
            {
                skipped.Add(new(key, reason));
                continue;
            }

            changes.Add(new($"{key}/ap", $"{references.Materia(type)} · slot {slot} AP",
                FieldValue.Of((long)gameplay.Ap(slot)), FieldValue.Of((long)ap)) { Derived = stars });
            var level = gameplay.StarLevel(slot, ap);
            if (level != gameplay.Level(slot))
            {
                changes.Add(new($"{key}/level", $"{references.Materia(type)} - slot {slot} stars",
                    FieldValue.Of((long)gameplay.Level(slot)), FieldValue.Of((long)level)) { Derived = !stars });
            }
        }

        return new()
        {
            Changes = changes, SkippedRows = skipped,
            Warnings = [WriteWarnings.Progression + "Ordinary stars are updated together with AP. Gear and ownership bytes are preserved.",
                "Materia labels: " + references.Source("ff7r-materia", type)],
        };
    }

    private string DefaultType() => Enumerable.Range(0, GameplayFields.MateriaCount)
        .Where(slot => gameplay.ApRange(slot) is not null).Select(slot => gameplay.TypeId(true, slot).ToString(CultureInfo.InvariantCulture))
        .FirstOrDefault() ?? "10001";

    private static ActionPlan Error(string field, string message) => new() { Errors = [new(field, message)] };
}
