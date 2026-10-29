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
    public const string RestoreAmmoActionId = "inventory/restore-imported-ammo";
    public const string QuantityParameter = "quantity";
    private const uint OldestObservedVersion = 47;
    private const uint LatestObservedVersion = 49;
    public const string CapacityWarning = TsushimaWriteWarnings.Console + " Experimental. " +
        "The game may clamp quantities to its capacity limits. Game carrying capacities and console persistence are unverified. " +
        "This sets a target quantity, not a capacity refill. Hide retains its raw name because its friendly mapping is unproven.";
    public const string RestoreAmmoWarning = TsushimaWriteWarnings.Console + " Experimental. Verified byte structure; console effect unverified. " +
        "Restore only reduced eligible ammo to this workspace's immutable opening counts, not a game maximum or a historical balance. " +
        "Counts already at or above opening stay unchanged. Re-importing establishes a new baseline. " +
        "The game may clamp quantities to its capacity limits. Game carrying capacities and console persistence are unverified; no capacity is granted.";

    // Capture values and eligibility once, before any edits. Never derive the baseline from mutable bytes on preview or view rebuild.
    private readonly IReadOnlyDictionary<string, uint> _openingAmmo = inventory?.Records
        .Where(record => record.IsAmmo && record.CanEdit(bytes))
        .ToDictionary(record => record.Name, record => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(record.QuantityOffset)),
            StringComparer.Ordinal) ?? [];

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

    public static ActionNode RestoreAmmoView => new(RestoreAmmoActionId, "Restore imported ammo counts (whole document)")
    {
        SupportsPlanning = true,
        Description = "Preview every eligible ammo row's own opening count, regardless of table selection. " +
            "Use Restore selected ammo counts inside the inventory table to limit restoration to selected rows. " +
            "Both count copies and the repaired checksum are shown. " +
            "Locked, oversized, inconsistent and non-ammo records are skipped. " + RestoreAmmoWarning,
    };

    public static ActionNode RestoreSelectedAmmoView => RestoreAmmoView with
    {
        Label = "Restore selected ammo counts",
        Description = "Preview only the inventory table's selected rows, including selections hidden by filters. " +
            "An empty table selection changes nothing; use the whole-document control to restore all eligible ammo. " +
            "Both count copies and the repaired checksum are shown. " +
            "Locked, oversized, inconsistent and non-ammo records are skipped. " + RestoreAmmoWarning,
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId is not (ActionId or InventoryActionId or RestoreAmmoActionId))
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

        var target = default(FieldValue);
        if (actionId == RestoreAmmoActionId)
        {
            if (parameters.Count != 0)
            {
                return Reject("Restoring imported ammo counts accepts no parameters.");
            }
        }
        else if (parameters.Count != 1 || !parameters.TryGetValue(QuantityParameter, out target) ||
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

        return PlanQuantities(inventory.Records, target, selected, actionId);
    }

    private ActionPlan PlanQuantities(IReadOnlyList<InventoryRecord> records, FieldValue requestedTarget, HashSet<string>? selected, string actionId)
    {
        var changes = new List<PlannedChange>();
        var mirrors = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        var restoreAmmo = actionId == RestoreAmmoActionId;
        var warnings = new List<string> { restoreAmmo ? RestoreAmmoWarning : CapacityWarning };
        var checksum = Word(8);
        foreach (var record in records.Where(record => selected?.Contains(record.Name) ?? IsSupported(record, actionId)))
        {
            if (!IsSupported(record, actionId))
            {
                skipped.Add(new(record.Name, actionId == ActionId ? "Excluded: this action supports only Supplies, Flowers and raw Hide." :
                    restoreAmmo ? "Excluded: this action supports only existing ammo records." :
                    "Excluded: this record is not a supported resource or ammo kind."));
                continue;
            }

            var current = Word(record.QuantityOffset);
            if (!record.CanEdit(bytes))
            {
                skipped.Add(new(record.Name, "Locked, oversized or inconsistent record; inspection only."));
                continue;
            }

            var target = requestedTarget;
            if (restoreAmmo)
            {
                if (!_openingAmmo.TryGetValue(record.Name, out var opening))
                {
                    skipped.Add(new(record.Name, "Imported ammo was locked, oversized or inconsistent; inspection only."));
                    continue;
                }
                target = FieldValue.Of((ulong)opening);
            }

            if (restoreAmmo ? current >= target.AsUInt32() : current == target.AsUInt32())
            {
                skipped.Add(new(record.Name, restoreAmmo ? "Already at or above the imported opening count." : "Already at the requested quantity."));
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

        AppendDerivedChanges(changes, mirrors, checksum);
        return new ActionPlan { Changes = changes, SkippedRows = skipped, Warnings = warnings };
    }

    private void AppendDerivedChanges(List<PlannedChange> changes, List<PlannedChange> mirrors, uint checksum)
    {
        if (changes.Count > 0)
        {
            changes.AddRange(mirrors);
            if (checksum != Word(8))
            {
                changes.Add(new(TsushimaIds.ChecksumValue, "Container checksum", FieldValue.Of((ulong)Word(8)), FieldValue.Of((ulong)checksum))
                { Derived = true });
            }
        }
    }

    private static bool IsSupported(InventoryRecord record, string actionId) => actionId switch
    {
        ActionId => record.Name is "Supplies" or "Flowers" or "Hide",
        RestoreAmmoActionId => record.IsAmmo,
        _ => record.IsResource || record.IsAmmo,
    };
    private static ActionPlan Reject(string message) => new() { Errors = [new(QuantityParameter, message)] };
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
