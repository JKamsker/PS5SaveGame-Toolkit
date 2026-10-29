using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithQuantityPlanning(byte[] bytes, FaithInventory inventory, FaithCatalog catalog)
{
    public const string ActionId = "set-main-quantities";
    public const string QuantityParameter = "quantity";
    // Maximum main-bank stack observed in the corpus, not a legal in-game cap.
    public const int MaximumTarget = FaithWriteLimits.MainQuantity;
    private const int QuantityOffset = sizeof(int);

    public static ActionNode Action() => new(ActionId, "Set existing main-bank quantities")
    {
        SupportsPlanning = true,
        Description = FaithWriteMap.Warning + $"Explicit target 0..{MaximumTarget} (observed corpus range, not a legal max/refill). " +
            "Preview lists every affected or skipped row. Item IDs, flags, allocation and secondary bank are unchanged.",
        Parameters = [new FieldNode(QuantityParameter, "Target stored quantity", new IntegerSpec(0, MaximumTarget), FieldValue.Of(0))],
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId != ActionId) return Error(actionId, "Unknown planned action.");
        if (parameters.Count != 1 || !parameters.TryGetValue(QuantityParameter, out var target) ||
            target.Kind != FieldValueKind.Integer || target.AsInteger() < 0 || target.AsInteger() > MaximumTarget)
            return Error(QuantityParameter, $"Target quantity must be an integer in 0..{MaximumTarget}; this is not a proven gameplay cap.");
        if (!inventory.IsSupported) return Error(actionId, "Main inventory schema/allocation is unsupported or ambiguous.");
        if (scope.QueryScope is { } query && (query.CollectionId != FaithInventoryView.CollectionId || scope.RowKeys is null))
            return Error(actionId, "Main-inventory query scopes must be resolved to explicit rows by Core before planning.");
        var rows = inventory.Items.ToDictionary(item => $"inventory/{item.Slot}", StringComparer.Ordinal);
        var selected = scope.RowKeys ?? rows.Keys.ToArray();
        if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Count || selected.Any(key => !rows.ContainsKey(key)))
            return Error(actionId, "Selection contains duplicate, foreign or unknown main-bank rows.");
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        var warnings = new List<string> { FaithWriteMap.Warning,
            "The explicit target uses conservative observed storage bounds. Active bank, pouch capacities, unique-item limits and progression effects remain unverified." };
        var candidate = bytes.ToArray();
        foreach (var key in selected)
        {
            var item = rows[key];
            var offset = item.Offset + QuantityOffset;
            var old = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
            if (old < 0)
            {
                skipped.Add(new(key, "Negative stack has unknown semantics; unchanged."));
                continue;
            }
            if (old == target.AsInt32())
            {
                skipped.Add(new(key, "Already at the requested quantity."));
                continue;
            }
            changes.Add(new(item.QuantityId, $"{catalog.Name(item.Id)} — main slot {item.Slot}", FieldValue.Of(old), target));
            if (FaithCapacityContext.IsUpgrade(item.Id)) changes.Add(new(FaithDerivedValues.UpgradeId(item, "main"),
                $"{catalog.Name(item.Id)} — raw upgrade quantity diagnostic", FieldValue.Of(old), target) { Derived = true });
            warnings.Add(FaithStackPolicy.Warning(item.Id, catalog.Category(item.Id)));
            BinaryPrimitives.WriteInt32LittleEndian(candidate.AsSpan(offset), target.AsInt32());
        }
        var oldCrc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(FaithContainer.CrcOffset));
        var newCrc = Crc32Ieee.Compute(candidate.AsSpan(FaithContainer.HeaderSize));
        if (newCrc != oldCrc) changes.Add(new("integrity/crc32", "Derived payload CRC32", FieldValue.Of((ulong)oldCrc),
            FieldValue.Of((ulong)newCrc)) { Derived = true });
        return new ActionPlan { Changes = changes, SkippedRows = skipped, Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray() };
    }

    private static ActionPlan Error(string id, string message) => new() { Errors = [new FieldError(id, message)] };
}
