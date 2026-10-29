using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class InventoryPlanning
{
    internal const string ActionId = "inventory/set-amount";
    internal const string RestoreActionId = "inventory/restore-imported-amounts";

    internal static ActionNode Action() => new(ActionId, "Set existing quantities (preview)") {
        SupportsPlanning = true, Tone = Tone.Warning,
        Description = "Set selected existing populated slots, or all eligible slots outside the table. Rows exceeding their observed container maximum are skipped. No slots are created or removed. " + InventoryState.WriteWarning,
        Parameters = [new FieldNode("amount", "Target quantity (Amount)", new IntegerSpec(0, long.MaxValue), FieldValue.Of(1))],
    };

    internal static ActionNode RestoreAction() => new(RestoreActionId, "Restore imported quantities (preview)") {
        SupportsPlanning = true, Tone = Tone.Warning,
        Description = "Restore eligible existing Amounts to their opening import values, only when lower. Equal or higher values stay unchanged. No slots are created or removed. " + InventoryState.WriteWarning,
    };

    internal static ActionPlan Plan(InventoryState state, string actionId,
        IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var restore = actionId == RestoreActionId;
        if (actionId is not (ActionId or RestoreActionId) || (scope.QueryScope is { } query &&
            (query.CollectionId != InventoryCollection.Id || scope.RowKeys is null)))
            return Error(actionId, "Choose inventory rows or a server-resolved inventory query scope.");
        var amount = default(FieldValue);
        if (restore && parameters.Count != 0)
            return Error(actionId, "Restore imported quantities does not accept parameters.");
        if (!restore && (parameters.Count != 1 || !parameters.TryGetValue("amount", out amount) ||
            amount.Kind != FieldValueKind.Integer || amount.AsInteger() < 0 || amount.AsInteger() > long.MaxValue))
            return Error("amount", "Target quantity must be a nonnegative signed 64-bit integer.");
        IEnumerable<InventoryItem> items = state.Items;
        if (scope.RowKeys is { } keys)
        {
            var ordinals = new int[keys.Count];
            for (var i = 0; i < keys.Count; i++)
                if (!state.TryGetOrdinal(keys[i], out ordinals[i])) return InvalidSelection(actionId);
            // Retain native model order, independent of selection order, without visiting
            // unselected slots. Sorting ordinals also makes duplicate identities adjacent.
            Array.Sort(ordinals);
            for (var i = 1; i < ordinals.Length; i++)
                if (ordinals[i] == ordinals[i - 1]) return InvalidSelection(actionId);
            items = ordinals.Select(ordinal => state.Items[ordinal]);
        }
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var item in items)
        {
            // Amount belongs to the immutable parsed model; Current reads the staged binding.
            var target = restore ? FieldValue.Of(item.Amount) : amount;
            var current = state.Current(item.Quantity);
            var reason = !state.CanSetAmount(item) ? "Empty or unsupported slot; no structure is changed." : state.ValidateAmount(item, target);
            if (reason is not null) skipped.Add(new(item.Key, reason));
            else if (restore && current.AsInteger() >= item.Amount)
                skipped.Add(new(item.Key, "Amount already equals or exceeds its opening import value."));
            else if (current == target) skipped.Add(new(item.Key, "Amount already equals the target."));
            else changes.Add(new(item.Key + "/amount", $"{item.Container}/{InventoryState.ShortId(state.Asset(item))} Amount (Inferred)",
                current, target));
        }
        var warnings = new List<string> { InventoryState.WriteWarning,
            "Bounds are original per-container observed maxima, not proven stack limits. Only Amount changes; item identity, equipment, resources/costs and ownership dependencies are unchanged." };
        if (restore) warnings.Add("Opening values belong to this import, not an earlier gameplay balance; re-import establishes a new baseline. Changed stored Kraken blocks may grow; output allocation limits do not guarantee console slot capacity.");
        return new ActionPlan { Changes = changes, SkippedRows = skipped, Warnings = warnings };
    }

    private static ActionPlan Error(string id, string message) => new() { Errors = [new FieldError(id, message)] };
    private static ActionPlan InvalidSelection(string id) => Error(id, "Selection must contain distinct existing inventory row keys.");
}
