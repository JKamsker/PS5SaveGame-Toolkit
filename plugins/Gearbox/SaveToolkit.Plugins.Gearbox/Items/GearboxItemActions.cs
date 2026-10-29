using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>F6 calculations are pure; F5 wraps every apply including new/removed identities.</summary>
internal sealed class GearboxItemActions(GearboxDocument document)
{
    public static IReadOnlyList<ViewNode> Build(bool rootWonderlands) =>
    [
        Action("items/level", "Set item levels", [new("level", "Target level", new IntegerSpec(1, rootWonderlands ? 40 : 72), FieldValue.Of(1))]),
        Action("items/duplicate", "Duplicate selected items", []),
        Action("items/remove", "Remove selected items", []),
        Action("items/import", "Import serial into backpack / bank", [new("code", "Serial code for this game", new TextSpec { MaxLength = 6000 }, FieldValue.Of(string.Empty))]),
    ];

    internal static ActionNode Action(string id, string title, IReadOnlyList<FieldNode> parameters) => new(id, title)
    { SupportsPlanning = true, Description = SerialWrites.Warning + " Structural actions require explicit item-row selection. Capacity is a conservative editor policy.", Parameters = parameters };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        try
        {
            var changes = new List<PlannedChange>();
            var skipped = new List<SkippedActionRow>();
            if (actionId == "items/import") PlanImport(parameters, scope, changes);
            else PlanSelected(actionId, parameters, scope, changes, skipped);
            return new() { Changes = changes, SkippedRows = skipped, Warnings = [SerialWrites.Warning,
                "Removal unequips matching slots and rebases surviving equipment indices. Import assigns seen/pickup metadata; duplicates clone all existing metadata. Account cloud mirrors are not reconciled."] };
        }
        catch (Exception ex) when (ex is SaveFormatException or FormatException or OverflowException)
        { return new() { Errors = [new("", ex.Message)], Warnings = [SerialWrites.Warning] }; }
    }

    private void PlanImport(IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, List<PlannedChange> changes)
    {
        if (scope.RowKeys is not null || scope.QueryScope is not null) throw new SaveFormatException("Import uses document scope.");
        var serial = Import(parameters);
        var list = document.Profile ? GearboxInventory.Bank : GearboxInventory.Backpack;
        document.Inventory.EnsureCapacity(list, 1);
        var count = document.Root.All(list).Count();
        changes.Add(new(list + "/count", "Import one validated-layout serial", FieldValue.Of(count), FieldValue.Of(count + 1)));
        changes.Add(new("import/serial", "New serial (game legality unverified)", FieldValue.Null, FieldValue.Of(Convert.ToBase64String(serial))) { OldExists = false });
    }

    private void PlanSelected(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope,
        List<PlannedChange> changes, List<SkippedActionRow> skipped)
    {
        if (actionId is not ("items/level" or "items/duplicate" or "items/remove")) throw new SaveFormatException("Unknown planned Gearbox item action.");
        var selected = Select(scope, actionId != "items/level");
        var level = actionId == "items/level" ? TargetLevel(parameters) : 0;
        foreach (var entry in selected)
        {
            if (actionId == "items/level")
            {
                try
                {
                    _ = SerialWrites.Set(entry.Serial, document.Root.Schema.Wonderlands, "level", level);
                    var old = document.Inventory.Info(entry.Id).Level!.Value;
                    if (old != level) changes.Add(new(entry.Id + "/level", entry.Id + " level", FieldValue.Of(old), FieldValue.Of(level)));
                }
                catch (SaveFormatException ex) { skipped.Add(new(entry.Id, ex.Message)); }
            }
            else
            {
                if (actionId == "items/duplicate" && !document.Inventory.Info(entry.Id).Complete)
                { skipped.Add(new(entry.Id, "Cannot create an item with an unknown or invalid layout.")); continue; }
                changes.Add(new(entry.Id + "/presence", entry.Id + (actionId == "items/remove" ? " removed" : " duplicated"),
                    FieldValue.Of(1), FieldValue.Of(actionId == "items/remove" ? 0 : 2)));
            }
        }
        var skippedIds = skipped.Select(row => row.RowKey).ToHashSet(StringComparer.Ordinal);
        if (actionId == "items/duplicate")
            foreach (var group in selected.Where(e => !skippedIds.Contains(e.Id)).GroupBy(e => e.List))
                document.Inventory.EnsureCapacity(group.Key, group.Count());
        if (actionId == "items/remove") AddSlotChanges(changes, selected);
    }

    public void Apply(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId == "items/import")
        {
            var serial = Import(parameters);
            document.Inventory.Append(document.Profile ? GearboxInventory.Bank : GearboxInventory.Backpack,
                new ProtoField(1, 2, null, [], serial, null));
        }
        else
        {
            var selected = Select(scope, actionId != "items/level").ToArray();
            var level = actionId == "items/level" ? TargetLevel(parameters) : 0;
            foreach (var entry in selected)
            {
                if (actionId == "items/remove") document.Inventory.Remove(entry.Id);
                else if (actionId == "items/duplicate")
                {
                    if (document.Inventory.Info(entry.Id).Complete)
                        document.Inventory.Append(entry.List, entry.Field);
                }
                else
                {
                    byte[] edited;
                    try { edited = SerialWrites.Set(entry.Serial, document.Root.Schema.Wonderlands, "level", level); }
                    catch (SaveFormatException) { continue; }
                    document.Inventory.SetSerial(entry.Id, edited);
                }
            }
        }
        document.InvalidateItems();
        _ = document.Serialize();
    }

    private InventoryEntry[] Select(ActionScope scope, bool requireSelection)
    {
        if (scope.QueryScope is not null && scope.RowKeys is null) throw new SaveFormatException("The host must resolve the query selection first.");
        if (requireSelection && scope.RowKeys is null) throw new SaveFormatException("Select explicit item rows before this structural action.");
        var entries = document.Inventory.Entries.ToArray();
        if (scope.RowKeys is null) return entries;
        var keys = scope.RowKeys.ToHashSet(StringComparer.Ordinal);
        var known = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        if (keys.Count != scope.RowKeys.Count || !keys.IsSubsetOf(known))
            throw new SaveFormatException("The selection contains stale, foreign or duplicate item rows.");
        return entries.Where(e => keys.Contains(e.Id)).ToArray();
    }

    private int TargetLevel(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("level", out var level) || level.Kind != FieldValueKind.Integer || level.AsInteger() < 1 ||
            level.AsInteger() > SerialWrites.Maximum(document.Root.Schema.Wonderlands, "level")) throw new SaveFormatException("Choose a supported target level.");
        return level.AsInt32();
    }

    private byte[] Import(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("code", out var code) || code.Kind != FieldValueKind.Text || code.AsString().Length > 6000)
            throw new SaveFormatException("Enter a bounded serial code for this game.");
        var text = code.AsString().Trim();
        var start = text.IndexOf('(');
        if (start >= 0 && text.EndsWith(')') && text[..start] is "BL3" or "WL" or "TTWL") text = text[(start + 1)..^1];
        var bytes = Convert.FromBase64String(text);
        if (!new SerialReader(document.Root.Schema.Wonderlands).Read(bytes).Complete)
            throw new SaveFormatException("Import requires a checksum-valid, fully mapped serial for this game's pinned database.");
        return bytes;
    }

    private void AddSlotChanges(List<PlannedChange> changes, InventoryEntry[] selected)
    {
        if (document.Profile) return;
        var removed = selected.Where(e => e.List == GearboxInventory.Backpack).Select(e => e.Index).ToArray();
        foreach (var (slot, index) in document.Root.Children("equipped_inventory_list").Select((s, i) => (s, i)))
        {
            var old = slot.Integer("inventory_list_index");
            var updated = removed.Contains((int)old) ? -1 : old - removed.Count(i => i < old);
            if (old != updated) changes.Add(new($"slots/{index}/index", $"Equipment slot {index} inventory index", FieldValue.Of(old), FieldValue.Of(updated)) { Derived = true });
        }
    }
}
