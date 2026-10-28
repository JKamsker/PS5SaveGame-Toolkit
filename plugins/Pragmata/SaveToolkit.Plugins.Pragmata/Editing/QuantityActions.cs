using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Editing;

/// <summary>Plans the existing fixed-size quantity operation; never grants records or unlocks.</summary>
internal sealed class QuantityActions(PragmataDocument document)
{
    private const string Risk = "Stored int32 quantities only. Item meanings, legal stack limits, unlock dependencies and console effects are Unknown. " +
        "Controlled in-game before/after captures are still required. Legal maxima and progression effects have not been established. " + PragmataDocument.EditWarning;

    // The fixed-size writer never moves a record. Physical identity also survives raw item-ID edits.
    public static string RowKey(SaveNode item) => $"inventory/record-at-{item.Offset:x}";

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        ActionPlan Reject(string message) => new() { Errors = [new FieldError(actionId, message)], Warnings = [Risk] };
        if (actionId != PragmataViews.QuantityAction || document.Layout.Inventory is null) return Reject("Unknown inventory action.");
        if (scope.QueryScope is { } query && (query.CollectionId != InventoryCollections.CollectionId || scope.RowKeys is null))
            return Reject("Use an inventory query scope resolved to row keys by the framework; evidence queries are not inventory selections.");
        if (parameters.Count != 1 || !parameters.TryGetValue("quantity", out var target) ||
            target.Kind != FieldValueKind.Integer || target.AsInteger() < 0 || target.AsInteger() > int.MaxValue)
            return Reject("Quantity must be an integer between 0 and 2147483647 (storage range, not a game limit).");
        if (document.Layout.Items.GroupBy(item => document.Read(item.Find(KnownHashes.ItemId)!)).Any(group => group.Count() > 1))
            return Reject("Duplicate inventory IDs: resolve raw-ID edits before planning quantities.");
        var inventory = document.Layout.Items.ToDictionary(RowKey, StringComparer.Ordinal);
        var keys = scope.RowKeys ?? inventory.Keys.ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(key => !inventory.ContainsKey(key)))
            return Reject("Selection contains duplicate or unknown inventory row keys.");
        var selected = keys.Select(key => inventory[key]).ToArray();
        if (selected.Any(item => !document.HasPrimaryBinding(item.Find(KnownHashes.Quantity)!)))
            return Reject("Expand the selected inventory quantity pages before previewing, so every edit can be undone.");
        var proposed = selected.ToDictionary(item => item.Find(KnownHashes.Quantity)!, _ => target);
        var primary = selected.ToDictionary(item => item.Find(KnownHashes.Quantity)!.Path, item =>
            $"Stored quantity · raw item ID 0x{document.Read(item.Find(KnownHashes.ItemId)!).AsUInt32():x8}", StringComparer.Ordinal);
        return new ActionPlan
        {
            Changes = document.ChangesFor(proposed).Select(change => new PlannedChange(change.ValueId,
                primary.GetValueOrDefault(change.ValueId) ?? $"Quantity alias · {change.ValueId}", change.OldValue, change.NewValue)
                { Derived = !primary.ContainsKey(change.ValueId) }).ToArray(),
            SkippedRows = selected.Where(item => document.Read(item.Find(KnownHashes.Quantity)!) == target)
                .Select(item => new SkippedActionRow(RowKey(item), "Stored quantity already equals the requested value.")).ToArray(),
            Warnings = [Risk, scope.QueryScope is not null
                ? $"All matching inventory rows: {selected.Length} resolved rows after exclusions, including unloaded pages. No records or unlock flags are added."
                : scope.RowKeys is null
                ? $"Document scope: {selected.Length} existing rows, including zero-count records. No records or unlock flags are added."
                : $"Explicit selection: {selected.Length} existing rows. No records or unlock flags are added."],
        };
    }

    public EditOutcome Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var fresh = Plan(actionId, parameters, scope);
        if (fresh.Errors.Count > 0) return new EditOutcome { Errors = fresh.Errors, Warnings = fresh.Warnings };
        if (plan.Errors.Count > 0 || !fresh.Changes.SequenceEqual(plan.Changes) || !fresh.SkippedRows.SequenceEqual(plan.SkippedRows) ||
            !fresh.Warnings.SequenceEqual(plan.Warnings))
            return EditOutcome.Rejected(actionId, "Quantity preview changed; preview the action again.");
        var outcome = document.ApplyEdits(fresh.Changes.Where(change => !change.Derived)
            .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());
        return outcome with { Warnings = fresh.Warnings };
    }
}
