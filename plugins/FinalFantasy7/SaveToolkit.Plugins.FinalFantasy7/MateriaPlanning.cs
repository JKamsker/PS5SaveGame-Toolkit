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

    public ActionNode View() => new(ActionId, "Set AP and derived stars")
    {
        SupportsPlanning = true,
        Description = "Choose a materia type, then preview AP changes on selected rows (or all owned copies). " +
            "Summons and mismatched rows are skipped. Ordinary stars follow the pinned AP curve. " + WriteWarnings.Experimental + " " + EmbeddedCatalogs.Attribution,
        Parameters =
        [
            new("type", "Materia type", new ReferenceSpec("ff7r-materia")
            {
                Assess = record => new ReferenceEligibility
                {
                    Eligible = uint.TryParse(record.Key, out var type) && Enumerable.Range(0, GameplayFields.MateriaCount)
                        .Any(slot => gameplay.TypeId(true, slot) == type && gameplay.ApRange(slot) is not null),
                    Reasons = ["Only owned, ordinary materia with coherent AP and stored stars can be edited."],
                },
            }, FieldValue.Of(DefaultType())) { Description = EmbeddedCatalogs.Attribution },
            new("ap", "Target AP", IntegerSpec.UInt32 with { Max = MateriaEditPolicy.MaximumTargetAp },
                FieldValue.Of((long)Enumerable.Range(0, GameplayFields.MateriaCount).Where(slot => gameplay.ApRange(slot) is not null)
                    .Select(gameplay.Ap).FirstOrDefault())),
        ],
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId != ActionId)
        {
            return Error("action", "Unknown FFVII planned action.");
        }

        if (scope.QueryScope is { } query && (query.CollectionId != "materia" || scope.RowKeys is null))
        {
            return Error("scope", "Only a Core-resolved materia query scope can be used for AP planning.");
        }

        uint type;
        uint ap;
        try
        {
            if (parameters.Count != 2 || !parameters.TryGetValue("type", out var typeValue) ||
                !uint.TryParse(typeValue.AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out type) ||
                !parameters.TryGetValue("ap", out var apValue) || (ap = apValue.AsUInt32()) > MateriaEditPolicy.MaximumTargetAp ||
                !EmbeddedCatalogs.Records(schema, "ff7r-materia").Any(record => record.Key == typeValue.AsString()))
            {
                return Error("parameters", "Choose a known materia type and AP between 0 and 999,999.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
        {
            return Error("parameters", "Invalid materia type or AP value.");
        }

        var keys = scope.RowKeys ?? Enumerable.Range(0, GameplayFields.MateriaCount)
            .Where(slot => gameplay.TypeId(true, slot) == type).Select(slot => $"materia/{slot}").ToArray();
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var key in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var parts = key.Split('/');
            if (parts.Length != 2 || parts[0] != "materia" || !int.TryParse(parts[1], NumberStyles.None,
                CultureInfo.InvariantCulture, out var slot) || slot < 0 || slot >= GameplayFields.MateriaCount || key != $"materia/{slot}")
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

            changes.Add(new($"{key}/ap", $"{references.Materia(type)} Â· slot {slot} AP",
                FieldValue.Of((long)gameplay.Ap(slot)), FieldValue.Of((long)ap)));
            var level = gameplay.StarLevel(slot, ap);
            if (level != gameplay.Level(slot))
            {
                changes.Add(new($"{key}/level", $"{references.Materia(type)} · slot {slot} stars",
                    FieldValue.Of((long)gameplay.Level(slot)), FieldValue.Of((long)level)) { Derived = true });
            }
        }

        return new()
        {
            Changes = changes, SkippedRows = skipped,
            Warnings = [WriteWarnings.Progression + "Ordinary stars are updated together with AP. Gear and ownership bytes are preserved."],
        };
    }

    private string DefaultType() => Enumerable.Range(0, GameplayFields.MateriaCount)
        .Where(slot => gameplay.ApRange(slot) is not null).Select(slot => gameplay.TypeId(true, slot).ToString(CultureInfo.InvariantCulture))
        .FirstOrDefault() ?? "10001";

    private static ActionPlan Error(string field, string message) => new() { Errors = [new(field, message)] };
}
