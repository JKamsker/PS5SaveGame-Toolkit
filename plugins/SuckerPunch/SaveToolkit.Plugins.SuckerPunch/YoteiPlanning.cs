using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed class YoteiPlanning(byte[] bytes, YoteiState? state, IReadOnlyDictionary<string, uint> limits,
    IReadOnlySet<string> editable)
{
    public const string ActionId = "yotei/inventory/set-quantity";
    private const string Parameter = "quantity";
    public static ActionNode View => new(ActionId, "Set existing Yōtei quantities")
    {
        SupportsPlanning = true,
        SelectionTableId = YoteiViews.InventorySelectionId,
        Description = "Preview a target quantity for selected existing supported records, including selections hidden by filters. " +
            "With nothing selected, cover all supported records in the inventory. Each record uses its own observed bound; " +
            "locked, unmapped, inconsistent and out-of-bound targets are skipped. Ammo copies and checksum are included. " + YoteiFormat.Warning,
        Parameters = [new FieldNode(Parameter, "Target quantity", new IntegerSpec(0, MaximumTarget), FieldValue.Of(0L))],
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId != ActionId || state is null) return Reject("This action requires a recognised Yōtei inventory.");
        if (parameters.Count != 1 || !parameters.TryGetValue(Parameter, out var target) ||
            target.Kind != FieldValueKind.Integer || target.AsInteger() < 0 || target.AsInteger() > MaximumTarget)
            return Reject("Supply exactly one integer quantity between 0 and 112099.");
        if (scope.QueryScope is not null) return Reject("The small inventory action does not accept query scopes; gear registries are read-only.");
        var selected = scope.RowKeys?.ToHashSet(StringComparer.Ordinal);
        if (selected is not null && selected.Any(key => !state.Inventory.Any(row => row.Name == key)))
            return Reject("Selection contains an unknown Yōtei inventory row.");
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        var oldChecksum = YoteiFormat.Word(bytes, YoteiFormat.ChecksumOffset);
        var checksum = oldChecksum;
        foreach (var row in state.Inventory.Where(row => selected?.Contains(row.Name) ?? row.ObservedMaximum.HasValue))
        {
            if (!editable.Contains(row.Name) || !limits.TryGetValue(row.Name, out var max))
            {
                skipped.Add(new(row.Name, "Unmapped, locked, inconsistent or checksum-mismatched record."));
                continue;
            }
            if (target.AsInteger() > max)
            {
                skipped.Add(new(row.Name, $"Target exceeds this record's conservative observed bound ({max})."));
                continue;
            }
            if (row.Quantity(bytes) == target.AsUInt32())
            {
                skipped.Add(new(row.Name, "Already at the requested quantity."));
                continue;
            }
            var before = FieldValue.Of((ulong)row.Quantity(bytes));
            changes.Add(new(row.QuantityId, row.Name, before, target));
            checksum = AdditiveChecksum.ReplaceWord(checksum, before.AsUInt32(), target.AsUInt32());
            if (row.Ammo)
            {
                changes.Add(new(row.CopyId, row.Name + " count copy", before, target) { Derived = true });
                checksum = AdditiveChecksum.ReplaceWord(checksum, before.AsUInt32(), target.AsUInt32());
            }
        }
        if (changes.Count > 0 && checksum != oldChecksum)
            changes.Add(new("container/word3", "Additive checksum candidate", FieldValue.Of((ulong)oldChecksum),
                FieldValue.Of((ulong)checksum)) { Derived = true });
        return new ActionPlan { Changes = changes, SkippedRows = skipped, Warnings = [YoteiFormat.Warning] };
    }

    private static ActionPlan Reject(string message) => new() { Errors = [new(Parameter, message)] };
    private const uint MaximumTarget = 112_099;
}
