using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class RepairPlanning(IReadOnlyList<InventoryItem> items, bool canEdit)
{
    public const string ActionId = "inventory/repair";
    public static ActionNode Action => new(ActionId, "Repair existing items (review)") {
        SupportsPlanning = true,
        Description = WriteFields.DurabilityWarning,
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId != ActionId) { throw new KeyNotFoundException($"Unknown planned action '{actionId}'."); }
        var errors = new List<FieldError>();
        if (!canEdit) { errors.Add(new(ActionId, "Unsupported player schema; this document is read-only.")); }
        if (parameters.Count != 0) { errors.Add(new(ActionId, "Repair has no parameters.")); }
        if (scope.QueryScope is { } query && (query.CollectionId != InventoryViews.CollectionId || scope.RowKeys is null))
        {
            errors.Add(new(ActionId, "A query scope must resolve to player-inventory row keys before planning."));
        }

        var byKey = items.ToDictionary(i => i.Id.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
        var keys = scope.RowKeys ?? items.Select(i => i.Id.ToString(CultureInfo.InvariantCulture)).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(k => !byKey.ContainsKey(k)))
        {
            errors.Add(new(ActionId, "Selection contains duplicate or unknown inventory instances."));
        }

        if (errors.Count > 0) { return new ActionPlan { Errors = errors, Warnings = [WriteFields.DurabilityWarning] }; }
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var key in keys)
        {
            var item = byKey[key];
            if (!item.CanEditDurability)
            {
                skipped.Add(new(key, "Sentinel or unsupported durability range; record is preserved."));
            }
            else if (item.Durability == InventoryItem.MaximumDurability)
            {
                skipped.Add(new(key, "Already at the conservative repair target (100)."));
            }
            else
            {
                changes.Add(new(item.DurabilityId, $"{item.Name} · Instance {item.Id} · Stored durability",
                    FieldValue.Of(item.Durability), FieldValue.Of((double)InventoryItem.MaximumDurability)));
            }
        }

        return new ActionPlan { Changes = changes, SkippedRows = skipped, Warnings = [WriteFields.DurabilityWarning] };
    }

    public static bool Matches(ActionPlan expected, ActionPlan supplied) => expected.Changes.SequenceEqual(supplied.Changes) &&
        expected.SkippedRows.SequenceEqual(supplied.SkippedRows) && expected.Warnings.SequenceEqual(supplied.Warnings) &&
        expected.Errors.SequenceEqual(supplied.Errors);
}
