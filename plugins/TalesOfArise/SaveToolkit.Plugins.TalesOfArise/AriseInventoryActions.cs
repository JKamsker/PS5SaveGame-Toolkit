using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>F6 planning for bounded changes to existing recognised consumable records.</summary>
internal sealed class AriseInventoryActions(AriseDocument document)
{
    public const string SetQuantityAction = "inventory/set-quantity";
    private const string QuantityParameter = "quantity";
    private const string Warning = "Experimental: the PS5 item mapping and 15-count stack bound are inferred from existing samples and PS4 research; console loading, later acquisition and zero-count semantics are unvalidated. " +
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

        var errors = new List<FieldError>();
        if (scope.QueryScope is not null)
        {
            errors.Add(new(actionId, "Query selection is unavailable for the fixed-slot inventory table; choose explicit existing row keys or document scope."));
        }

        if (!document.InventoryBindingsReady)
        {
            errors.Add(new(actionId, "Expand the inventory table before planning so every quantity change has a live undo binding."));
        }

        if (parameters.Count != 1 || !parameters.TryGetValue(QuantityParameter, out var candidate)
            || candidate.Kind != FieldValueKind.Integer || candidate.AsInteger() < 0 || candidate.AsInteger() > AriseDocument.ConsumableStackLimit)
        {
            errors.Add(new(QuantityParameter, $"Quantity must be a whole number from 0 to {AriseDocument.ConsumableStackLimit}."));
        }

        if (errors.Count > 0)
        {
            return new ActionPlan { Errors = errors, Warnings = [Warning] };
        }

        var target = parameters[QuantityParameter];
        var rows = document.ReadInventoryRows();
        var byKey = rows.ToDictionary(RowKey, StringComparer.Ordinal);
        var keys = scope.RowKeys ?? rows.Select(RowKey).ToArray();
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
            if (row.Name is null)
            {
                skipped.Add(new(key, "Numeric ID is not one of the recognised consumable mappings; preserved."));
                continue;
            }

            if (row.Quantity > AriseDocument.ConsumableStackLimit)
            {
                skipped.Add(new(key, $"Existing quantity {row.Quantity} exceeds the inferred {AriseDocument.ConsumableStackLimit} bound; preserved."));
                continue;
            }

            var oldValue = FieldValue.Of((long)row.Quantity);
            if (oldValue == target)
            {
                skipped.Add(new(key, "Existing quantity already equals the requested value."));
                continue;
            }

            changes.Add(new PlannedChange(key, $"{row.Name} · quantity (raw ID 0x{row.Id:X4})", oldValue, target));
        }

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

    public EditOutcome Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var fresh = Plan(actionId, parameters, scope);
        if (fresh.Errors.Count > 0)
        {
            return new EditOutcome { Errors = fresh.Errors, Warnings = fresh.Warnings };
        }

        if (plan.Errors.Count > 0 || !fresh.Changes.SequenceEqual(plan.Changes)
            || !fresh.SkippedRows.SequenceEqual(plan.SkippedRows) || !fresh.Warnings.SequenceEqual(plan.Warnings))
        {
            return EditOutcome.Rejected(actionId, "The quantity preview changed; preview the action again.") with { Warnings = fresh.Warnings };
        }

        document.EnsureInventoryBindings();
        var result = document.ApplyEdits(fresh.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());
        return result with { Warnings = [.. fresh.Warnings, .. result.Warnings] };
    }

    public static string RowKey(AriseInventoryRow row) => $"inventory/{row.Index}/quantity";
}

internal sealed record AriseInventoryRow(int Index, int Offset, ushort Id, byte Quantity, string? Name);
