using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>F6 planning for bounded changes to existing recognised consumable records.</summary>
internal sealed class AriseInventoryActions(AriseDocument document)
{
    public const string SetQuantityAction = "inventory/set-quantity";
    private const string QuantityParameter = "quantity";
    private const string Warning = AriseDocument.ConsoleWarning + "The PS5 item mapping and 15-count stack bound are inferred from existing samples and PS4 research; console loading, later acquisition and zero-count semantics are unvalidated. " +
        "Only existing recognised records change. Ciphertext SHA1 and encryption are recomputed and self-checked; the game may still reject the edited save.";

    public static bool Handles(string actionId) => actionId == SetQuantityAction;

    public static ActionNode View() => new(SetQuantityAction, "Set existing consumable quantities")
    {
        SupportsPlanning = true,
        Tone = Tone.Warning,
        Description = "Expand the inventory table, then preview a selected set of existing recognised consumable rows, or all such rows in this save. Unknown/equipment rows, records above the observed bound and new records are skipped. The batch is one undo step.",
        Parameters = [new FieldNode(QuantityParameter, "Quantity", new IntegerSpec(0, AriseDocument.ConsumableStackLimit), FieldValue.Of((long)AriseDocument.ConsumableStackLimit))],
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

        return PlanSelection(actionId, parameters[QuantityParameter], scope);
    }

    private ActionPlan PlanSelection(string actionId, FieldValue target, ActionScope scope)
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

        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var key in keys)
        {
            var row = byKey[key];
            var reason = SkipReason(row, target);
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
            Warnings = [Warning, selectionDescription + " No IDs, flags, order or records are added or removed."],
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

        if (parameters.Count != 1 || !parameters.TryGetValue(QuantityParameter, out var candidate)
            || candidate.Kind != FieldValueKind.Integer || candidate.AsInteger() < 0 || candidate.AsInteger() > AriseDocument.ConsumableStackLimit)
        {
            errors.Add(new(QuantityParameter, $"Quantity must be a whole number from 0 to {AriseDocument.ConsumableStackLimit}."));
        }

        return errors;
    }

    private static string? SkipReason(AriseInventoryRow row, FieldValue target)
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
