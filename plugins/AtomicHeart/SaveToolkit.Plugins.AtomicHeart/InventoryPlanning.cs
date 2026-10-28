using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class InventoryPlanning
{
    internal const string ActionId = "inventory/set-amount";

    internal static ActionNode Action() => new(ActionId, "Set existing amounts (preview)") {
        SupportsPlanning = true, Tone = Tone.Warning,
        Description = "Set selected existing populated slots, or all eligible slots outside the table. Rows exceeding their observed container maximum are skipped. No slots are created or removed. " + InventoryState.WriteWarning,
        Parameters = [new FieldNode("amount", "Target Amount", new IntegerSpec(0, long.MaxValue), FieldValue.Of(1))],
    };

    internal static ActionPlan Plan(InventoryState state, string actionId,
        IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId != ActionId || (scope.QueryScope is { } query &&
            (query.CollectionId != InventoryCollection.Id || scope.RowKeys is null)))
            return Error(actionId, "Choose inventory rows or a server-resolved inventory query scope.");
        if (parameters.Count != 1 || !parameters.TryGetValue("amount", out var amount) ||
            amount.Kind != FieldValueKind.Integer || amount.AsInteger() < 0 || amount.AsInteger() > long.MaxValue)
            return Error("amount", "Target Amount must be a nonnegative signed 64-bit integer.");
        IEnumerable<InventoryItem> items = state.Items;
        if (scope.RowKeys is { } keys)
        {
            var selected = keys.ToHashSet(StringComparer.Ordinal);
            if (selected.Count != keys.Count || selected.Except(state.Items.Select(i => i.Key), StringComparer.Ordinal).Any())
                return Error(actionId, "Selection must contain distinct existing inventory row keys.");
            items = items.Where(item => selected.Contains(item.Key));
        }
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var item in items)
        {
            var reason = !state.CanSetAmount(item) ? "Empty or unsupported slot; no structure is changed." : state.ValidateAmount(item, amount);
            if (reason is not null) skipped.Add(new(item.Key, reason));
            else if (state.Current(item.Quantity) == amount) skipped.Add(new(item.Key, "Amount already equals the target."));
            else changes.Add(new(item.Key + "/amount", $"{item.Container}/{InventoryState.ShortId(state.Asset(item))} Amount (Inferred)",
                state.Current(item.Quantity), amount));
        }
        return new ActionPlan { Changes = changes, SkippedRows = skipped, Warnings = [InventoryState.WriteWarning,
            "Bounds are original per-container observed maxima, not proven stack limits. Only Amount changes; item identity, equipment, resources/costs and ownership dependencies are unchanged."] };
    }

    internal static bool Equivalent(ActionPlan left, ActionPlan right) => left.Changes.SequenceEqual(right.Changes) &&
        left.SkippedRows.SequenceEqual(right.SkippedRows) && left.Warnings.SequenceEqual(right.Warnings) && left.Errors.SequenceEqual(right.Errors);

    private static ActionPlan Error(string id, string message) => new() { Errors = [new FieldError(id, message)] };
}
