using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Editing;

/// <summary>Plans fixed-size quantity targets and imported restoration; never grants records or unlocks.</summary>
internal sealed class QuantityActions(PragmataDocument document)
{
    private const string Risk = PragmataDocument.EditWarning + " Inferred stored int32 quantities only. Item meanings, legal stack limits, unlock dependencies and console effects are Unknown. " +
        "Controlled in-game before/after captures are still required. Legal maxima and progression effects have not been established.";
    private const string RestoreWarning = "Restore uses each unchanged raw item ID's immutable workspace opening quantity, only when the current quantity is lower and both are nonnegative. " +
        "Values above that baseline stay unchanged. This cannot recover amounts lost before import; re-import establishes a new opening baseline.";

    // The fixed-size writer never moves a record. Physical identity also survives raw item-ID edits.
    public static string RowKey(SaveNode item) => $"inventory/record-at-{item.Offset:x}";

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var restore = actionId == PragmataViews.RestoreQuantityAction;
        var warnings = restore ? new[] { Risk, RestoreWarning } : [Risk];
        ActionPlan Reject(string message) => new() { Errors = [new FieldError(actionId, message)], Warnings = warnings };
        if ((!restore && actionId != PragmataViews.QuantityAction) || document.Layout.Inventory is null) return Reject("Unknown inventory action.");
        if (scope.QueryScope is { } query && (query.CollectionId != InventoryCollections.CollectionId || scope.RowKeys is null))
            return Reject("Use an inventory query scope resolved to row keys by the framework; evidence queries are not inventory selections.");
        var target = FieldValue.Null;
        if (restore && parameters.Count != 0) return Reject("Restore imported quantities takes no parameters.");
        if (!restore && (parameters.Count != 1 || !parameters.TryGetValue("quantity", out target) ||
            target.Kind != FieldValueKind.Integer || target.AsInteger() < 0 || target.AsInteger() > int.MaxValue))
            return Reject("Quantity must be an integer between 0 and 2147483647 (storage range, not a game limit).");
        if (document.Layout.Items.GroupBy(item => document.Read(item.Find(KnownHashes.ItemId)!)).Any(group => group.Count() > 1))
            return Reject("Duplicate inventory IDs: resolve raw-ID edits before planning quantities.");
        if (restore && document.Layout.Items.GroupBy(item => document.ReadImported(item.Find(KnownHashes.ItemId)!)).Any(group => group.Count() > 1))
            return Reject("Duplicate opening inventory IDs: imported targets are ambiguous.");
        var inventory = document.Layout.Items.ToDictionary(RowKey, StringComparer.Ordinal);
        var keys = scope.RowKeys ?? inventory.Keys.ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(key => !inventory.ContainsKey(key)))
            return Reject("Selection contains duplicate or unknown inventory row keys.");
        var selected = keys.Select(key => inventory[key]).ToArray();
        if (selected.Any(item => !document.HasPrimaryBinding(item.Find(KnownHashes.Quantity)!)))
            return Reject("Expand the selected inventory quantity pages before previewing, so every edit can be undone.");
        var proposed = new Dictionary<SaveNode, FieldValue>(selected.Length);
        var skipped = new List<SkippedActionRow>();
        foreach (var item in selected)
        {
            var quantity = item.Find(KnownHashes.Quantity)!;
            var current = document.Read(quantity);
            var rowTarget = restore ? document.ReadImported(quantity) : target;
            var reason = restore ? RestoreSkipReason(item, quantity, current, rowTarget)
                : current == rowTarget ? "Stored quantity already equals the requested value." : null;
            if (reason is not null) skipped.Add(new SkippedActionRow(RowKey(item), reason));
            else proposed.Add(quantity, rowTarget);
        }
        var primary = selected.ToDictionary(item => item.Find(KnownHashes.Quantity)!.Path, item =>
            $"Stored quantity · raw item ID 0x{document.Read(item.Find(KnownHashes.ItemId)!).AsUInt32():x8}", StringComparer.Ordinal);
        return new ActionPlan
        {
            Changes = document.ChangesFor(proposed).Select(change => new PlannedChange(change.ValueId,
                primary.GetValueOrDefault(change.ValueId) ?? $"Quantity alias · {change.ValueId}", change.OldValue, change.NewValue)
                { Derived = !primary.ContainsKey(change.ValueId) }).ToArray(),
            SkippedRows = skipped.ToArray(),
            Warnings = [.. warnings, scope.QueryScope is not null
                ? $"All matching inventory rows: {selected.Length} resolved rows after exclusions, including unloaded pages. No records or unlock flags are added."
                : scope.RowKeys is null
                ? $"Document scope: {selected.Length} existing rows, including zero-count records. No records or unlock flags are added."
                : $"Explicit selection: {selected.Length} existing rows. No records or unlock flags are added."],
        };
    }

    private string? RestoreSkipReason(SaveNode item, SaveNode quantity, FieldValue current, FieldValue imported)
    {
        var id = item.Find(KnownHashes.ItemId)!;
        if (document.Read(id) != document.ReadImported(id)) return "Raw item ID changed since opening; imported quantity does not identify this item.";
        if (imported.AsInt32() < 0) return "The opening quantity is negative; restoration is ineligible.";
        if (current.AsInt32() < 0) return "The current quantity is negative; restoration is ineligible.";
        if (document.Bounds.Validate(quantity, imported) is { } error) return error;
        if (current == imported) return "Stored quantity already equals the opening quantity.";
        return current.AsInt32() > imported.AsInt32() ? "Stored quantity is above the opening quantity; preserved unchanged." : null;
    }

    public EditOutcome Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
        ActionPlans.ApplyScalar(plan, () => Plan(actionId, parameters, scope),
            new FieldError(actionId, "Quantity preview changed; preview the action again."),
            prepareBindings: _ => { }, // Planning retains the primary-binding guard; F8 publishes specs before planning.
            selectEdits: fresh => fresh.Changes.Where(change => !change.Derived)
                .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray(),
            applyEdits: document.ApplyEdits, warningPolicy: ActionPlanWarningPolicy.UsePlan);
}
