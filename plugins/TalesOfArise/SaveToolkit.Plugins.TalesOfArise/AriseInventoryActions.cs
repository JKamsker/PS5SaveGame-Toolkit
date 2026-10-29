using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>F6 planning for bounded changes to existing recognised consumable records.</summary>
internal sealed class AriseInventoryActions(AriseDocument document)
{
    public const string SetQuantityAction = "inventory/set-quantity";
    public const string RestoreImportedAction = "inventory/restore-imported";
    private const string QuantityParameter = "quantity";
    private const string RestoreWarning = "Restore uses this workspace's immutable opening quantities, not a game maximum or an earlier save. " +
        "Only lower quantities are raised; quantities already at or above opening values are preserved. Re-importing an edited save establishes a new baseline.";
    private const string Warning = AriseDocument.ConsoleWarning + "The PS5 item mapping and 15-count stack bound are inferred from existing samples and PS4 research; console loading, later acquisition and zero-count semantics are unvalidated. " +
        "Only existing recognised records change. Ciphertext SHA1 and encryption are recomputed and self-checked; the game may still reject the edited save.";

    public static bool Handles(string actionId) => actionId is SetQuantityAction or RestoreImportedAction;

    public static ActionNode View() => new(SetQuantityAction, "Set existing consumable quantities")
    {
        SupportsPlanning = true,
        SelectionTableId = AriseInventoryQuery.CollectionId,
        Tone = Tone.Warning,
        Description = "Expand the inventory table, then preview its selected rows or all matching query rows minus exclusions. With no selection, preview all existing recognised consumables in this save. An explicitly empty selection changes nothing. Unknown/equipment rows, records above the observed bound and new records are skipped. The batch is one undo step. " + Warning,
        Parameters = [new FieldNode(QuantityParameter, "Quantity", new IntegerSpec(0, AriseDocument.ConsumableStackLimit), FieldValue.Of((long)AriseDocument.ConsumableStackLimit))],
    };

    public static ActionNode RestoreImportedView() => new(RestoreImportedAction, "Restore imported consumable quantities")
    {
        SupportsPlanning = true,
        Tone = Tone.Warning,
        Description = "Preview restoring selected existing recognised consumables to this workspace's opening quantities, only when lower. " +
            "Imported and current quantities must both be within the inferred 0–15 bound. Equipment, unknown IDs and higher quantities are preserved. " + RestoreWarning + " " + Warning,
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (!Handles(actionId))
        {
            throw new KeyNotFoundException($"Unknown Tales of Arise planned action '{actionId}'.");
        }

        var errors = ValidateRequest(actionId, parameters, scope);
        if (errors.Count > 0)
        {
            return new ActionPlan { Errors = errors, Warnings = [Warning] };
        }

        return PlanSelection(actionId, parameters, scope);
    }

    private ActionPlan PlanSelection(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var rows = document.ReadInventoryRows();
        var byKey = rows.ToDictionary(RowKey, StringComparer.Ordinal);
        var keys = scope.RowKeys ?? rows.Where(row => !row.IsEmpty).Select(RowKey).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(key => !byKey.ContainsKey(key)))
        {
            return new ActionPlan
            {
                Errors = [new(actionId, "Selection contains duplicate or unknown inventory row keys.")],
                Warnings = [Warning],
            };
        }

        var restoreImported = actionId == RestoreImportedAction;
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var key in keys)
        {
            var row = byKey[key];
            var target = restoreImported ? FieldValue.Of((long)document.ImportedInventoryQuantity(row)) : parameters[QuantityParameter];
            var reason = SkipReason(row, target, restoreImported);
            if (reason is not null)
            {
                skipped.Add(new(key, reason));
                continue;
            }

            var oldValue = FieldValue.Of((long)row.Quantity);
            changes.Add(new PlannedChange(key, $"{row.Name} · quantity (raw ID 0x{row.Id:X4})", oldValue, target));
        }

        changes.AddRange(document.PlanReadOnlyAliases(changes.ToDictionary(change => byKey[change.ValueId].Offset,
            change => checked((byte)change.NewValue.AsUInt32()))));
        var selectionDescription = scope.RowKeys is null
            ? $"Document scope selected {keys.Count} existing inventory rows."
            : $"Explicit selection contains {keys.Count} existing inventory rows.";
        return new ActionPlan
        {
            Changes = changes,
            SkippedRows = skipped,
            Warnings = actionId == RestoreImportedAction
                ? [Warning, RestoreWarning, selectionDescription + " No IDs, flags, order or records are added or removed."]
                : [Warning, selectionDescription + " No IDs, flags, order or records are added or removed."],
        };
    }

    private List<FieldError> ValidateRequest(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var errors = new List<FieldError>();
        if (scope.QueryScope is { } queryScope && queryScope.CollectionId != AriseInventoryQuery.CollectionId)
        {
            errors.Add(new(actionId, $"Query selection must target the '{AriseInventoryQuery.CollectionId}' collection."));
        }

        if (scope.QueryScope is not null && scope.RowKeys is null)
        {
            errors.Add(new(actionId, "The framework must resolve the query selection to stable inventory row keys before planning."));
        }

        if (!document.InventoryExpanded && scope.QueryScope is null)
        {
            errors.Add(new(actionId, "Expand the inventory table before planning a document or explicit selection, or use an inventory query scope."));
        }

        if (actionId == RestoreImportedAction)
        {
            if (parameters.Count != 0) errors.Add(new(actionId, "Restore imported quantities does not accept parameters."));
        }
        else if (parameters.Count != 1 || !parameters.TryGetValue(QuantityParameter, out var candidate)
            || candidate.Kind != FieldValueKind.Integer || candidate.AsInteger() < 0 || candidate.AsInteger() > AriseDocument.ConsumableStackLimit)
        {
            errors.Add(new(QuantityParameter, $"Quantity must be a whole number from 0 to {AriseDocument.ConsumableStackLimit}."));
        }

        return errors;
    }

    private static string? SkipReason(AriseInventoryRow row, FieldValue target, bool restoreImported)
    {
        if (row.IsEmpty)
        {
            return "Empty slot; no item is inserted or changed.";
        }

        if (row.Name is null)
        {
            return "Numeric ID is not one of the recognised consumable mappings; preserved.";
        }

        if (row.Quantity > AriseDocument.ConsumableStackLimit)
        {
            return $"Existing quantity {row.Quantity} exceeds the inferred {AriseDocument.ConsumableStackLimit} bound; preserved.";
        }

        if (restoreImported)
        {
            if (target.AsInteger() > AriseDocument.ConsumableStackLimit)
                return $"Imported quantity exceeds the inferred {AriseDocument.ConsumableStackLimit} bound; preserved.";
            return row.Quantity >= target.AsInteger()
                ? "Existing quantity is already at or above its opening quantity; preserved."
                : null;
        }

        return FieldValue.Of((long)row.Quantity) == target
            ? "Existing quantity already equals the requested value."
            : null;
    }

    public EditOutcome Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
        ActionPlans.ApplyScalar(plan, () => Plan(actionId, parameters, scope),
            new FieldError(actionId, "The quantity preview changed; preview the action again."),
            document.EnsureInventoryBindings,
            fresh => fresh.Changes.Where(change => !change.Derived)
                .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray(),
            document.ApplyEdits, ActionPlanWarningPolicy.Merge);

    public static string RowKey(AriseInventoryRow row) => $"inventory/{row.Index}/quantity";
}

internal sealed record AriseInventoryRow(int Index, int Offset, ushort Id, byte Quantity, byte Flags, string? Name)
{
    public const string EmptySlotLabel = "Empty slot";
    public bool IsEmpty => Id == 0;
}
