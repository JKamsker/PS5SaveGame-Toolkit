using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Original quantities are document-local ceilings, never legal stack-cap assertions.</summary>
internal sealed class RefillPlanning(IReadOnlyList<InventoryItem> items, bool canEdit)
{
    public const string ActionId = "inventory/refill-imported";
    public const string Warning = WriteFields.ConsoleWarning + " Restores existing ordinary Bread/Bottled water stacks only up to their imported quantity. " +
        "This is not a legal-cap, acquisition, vendor or quest transaction. Unknown, quest, currency, equipment and potion records are skipped.";
    public static ActionNode Action => new(ActionId, "Refill ordinary food/water to imported quantity (review)") {
        SupportsPlanning = true, Description = Warning,
    };

    public ActionPlan Plan(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var (selected, errors) = InventoryActionSelection.Resolve(ActionId, items, canEdit, parameters, scope);
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        if (errors.Count == 0)
        {
            foreach (var item in selected)
            {
                var key = InventoryActionSelection.Key(item);
                // Exact ordinary native IDs observed in the corpus; never match translated labels or quest-prefixed variants.
                if (item.Name is not ("Bread" or "Bottled water") || item.Equipped || item.Slot.Length > 0 || item.Tags.Count > 0)
                {
                    skipped.Add(new(key, "Not an explicitly supported ordinary unequipped, untagged food/water stack."));
                }
                else if (item.Quantity >= item.OriginalQuantity)
                {
                    skipped.Add(new(key, "Already at or above imported quantity; a refill never decreases a stack."));
                }
                else
                {
                    changes.Add(new(item.ValueId, $"{item.Name} · Instance {item.Id} · Quantity",
                        FieldValue.Of(item.Quantity), FieldValue.Of(item.OriginalQuantity)));
                }
            }
        }
        return new ActionPlan { Changes = changes, SkippedRows = skipped, Errors = errors, Warnings = [Warning] };
    }
}
