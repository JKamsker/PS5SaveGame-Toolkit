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
        var (selected, errors) = InventoryActionSelection.Resolve(ActionId, items, canEdit, parameters, scope);
        if (errors.Count > 0) { return new ActionPlan { Errors = errors, Warnings = [WriteFields.DurabilityWarning] }; }
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var item in selected)
        {
            var key = InventoryActionSelection.Key(item);
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
