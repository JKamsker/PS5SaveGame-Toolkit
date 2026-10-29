using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Plans existing resource/ammo quantities; it never unlocks records or interprets carrying capacities.</summary>
internal sealed class TsushimaQuantityPlanning(byte[] bytes, SaveContainer container, TsushimaInventory? inventory)
{
    public const string ActionId = "inventory/set-goods-quantity";
    public const string InventoryActionId = "inventory/set-quantity";
    public const string QuantityParameter = "quantity";
    private const uint OldestObservedVersion = 47;
    private const uint LatestObservedVersion = 49;
    public const string CapacityWarning = TsushimaWriteWarnings.Console + " Experimental. " +
        "The game may clamp quantities to its capacity limits. Game carrying capacities and console persistence are unverified. " +
        "This sets a target quantity, not a capacity refill. Hide retains its raw name because its friendly mapping is unproven.";

    public static ActionNode View => new(ActionId, "Set existing goods quantity")
    {
        SupportsPlanning = true,
        Description = "Preview one target quantity for existing Supplies, Flowers and raw Hide records. " +
            "Locked or oversized records are skipped. Other inventory IDs are excluded. " + CapacityWarning,
        Parameters = [new FieldNode(QuantityParameter, "Target quantity", new IntegerSpec(0, InventoryRecord.MaximumQuantity), FieldValue.Of(0L))],
    };

    public static ActionNode InventoryView => View with
    {
        ActionId = InventoryActionId,
        Label = "Set existing resource/ammo quantities",
        Description = "Preview one target quantity for existing supported resources and ammo. Both ammo count words change together. " +
            "Locked, oversized, unmapped and inconsistent records are skipped. " + CapacityWarning,
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId is not (ActionId or InventoryActionId))
        {
            throw new KeyNotFoundException($"Unknown action '{actionId}'.");
        }

        if (inventory is null || container.Kind != ContainerKind.Tsushima)
        {
            return Reject("Quantity planning is available only for the recognized Tsushima inventory.");
        }

        if (container.Word3 is < OldestObservedVersion or > LatestObservedVersion)
        {
            return Reject("Quantity planning is limited to observed Tsushima container versions 47–49.");
        }

        if (parameters.Count != 1 || !parameters.TryGetValue(QuantityParameter, out var target) ||
            target.Kind != FieldValueKind.Integer || target.AsInteger() < 0 || target.AsInteger() > InventoryRecord.MaximumQuantity)
        {
            return Reject("Supply exactly one integer target quantity between 0 and 32767.");
        }

        if (scope.QueryScope is not null)
        {
            return Reject("This bounded inventory action does not accept query scopes.");
        }

        var selected = scope.RowKeys is null ? null : new HashSet<string>(scope.RowKeys, StringComparer.Ordinal);
        if (selected is not null && selected.Any(key => !inventory.Records.Any(record => record.Name == key)))
        {
            return Reject("Selection contains an unknown inventory row.");
        }

        var goodsOnly = actionId == ActionId;
        var changes = new List<PlannedChange>();
        var mirrors = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        var warnings = new List<string> { CapacityWarning };
        var checksum = Word(8);
        foreach (var record in inventory.Records.Where(record => selected?.Contains(record.Name) ?? IsSupported(record, goodsOnly)))
        {
            if (!IsSupported(record, goodsOnly))
            {
                skipped.Add(new(record.Name, goodsOnly ? "Excluded: this action supports only Supplies, Flowers and raw Hide." :
                    "Excluded: this record is not a supported resource or ammo kind."));
                continue;
            }

            var current = Word(record.QuantityOffset);
            if (!record.CanEdit(bytes))
            {
                skipped.Add(new(record.Name, "Locked, oversized or inconsistent record; inspection only."));
                continue;
            }

            if (current == target.AsUInt32())
            {
                skipped.Add(new(record.Name, "Already at the requested quantity."));
                continue;
            }

            changes.Add(new(record.ValueId, record.Name, FieldValue.Of((ulong)current), target));
            checksum = AdditiveChecksum.ReplaceWord(checksum, current, target.AsUInt32());
            if (record.IsAmmo)
            {
                mirrors.Add(new(record.MirrorValueId, $"{record.Name} count copy", FieldValue.Of((ulong)current), target) { Derived = true });
                checksum = AdditiveChecksum.ReplaceWord(checksum, current, target.AsUInt32());
            }
        }

        if (changes.Count > 0)
        {
            changes.AddRange(mirrors);
            if (checksum != Word(8))
            {
                changes.Add(new(TsushimaIds.ChecksumValue, "Container checksum", FieldValue.Of((ulong)Word(8)), FieldValue.Of((ulong)checksum))
                { Derived = true });
            }
        }

        return new ActionPlan { Changes = changes, SkippedRows = skipped, Warnings = warnings };
    }

    public static bool Matches(ActionPlan expected, ActionPlan supplied) =>
        expected.Errors.Count == 0 && supplied.Errors.Count == 0 &&
        expected.Changes.SequenceEqual(supplied.Changes) && expected.SkippedRows.SequenceEqual(supplied.SkippedRows) &&
        expected.Warnings.SequenceEqual(supplied.Warnings);

    private static bool IsSupported(InventoryRecord record, bool goodsOnly) => goodsOnly
        ? record.Name is "Supplies" or "Flowers" or "Hide" : record.IsResource || record.IsAmmo;
    private static ActionPlan Reject(string message) => new() { Errors = [new(QuantityParameter, message)] };
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
