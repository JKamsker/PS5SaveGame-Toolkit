using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>F6 preview/apply inside F7 staging; the group's complete F5 snapshot supplies history.</summary>
internal sealed class GearboxTransferActions(GearboxGroupDocument group)
{
    public static IReadOnlyList<ViewNode> Build() =>
    [
        GearboxItemActions.Action("transfer/deposit", "Move selected backpack items to bank", []) with
        { SelectionTableId = GearboxGroups.CharacterRole + "/" + GearboxInventory.Backpack },
        GearboxItemActions.Action("transfer/withdraw", "Move selected bank items to backpack", []) with
        { SelectionTableId = GearboxGroups.ProfileRole + "/" + GearboxInventory.Bank },
        GearboxItemActions.Action("transfer/claim", "Claim selected lost-loot items", []) with
        { SelectionTableId = GearboxGroups.ProfileRole + "/" + GearboxInventory.LostLoot },
    ];

    public ActionPlan Plan(string actionId, ActionScope scope)
    {
        try
        {
            var (source, target, sourceList, targetList, role) = Route(actionId);
            var entries = Select(source, sourceList, role, scope);
            target.Inventory.EnsureCapacity(targetList, entries.Length);
            var changes = new List<PlannedChange>();
            foreach (var entry in entries)
            {
                if (!source.Inventory.Info(entry.Id).Complete)
                    throw new SaveFormatException("Transfers require fully mapped checksum-valid serials; unknown items remain in their original container.");
                target.Inventory.ValidateDestination(targetList, entry.Field);
                changes.Add(new(role + "/" + sourceList + "/" + entry.Id, "Move " + entry.Id + " (original serial retained)",
                    FieldValue.Of(Convert.ToBase64String(entry.Serial)), FieldValue.Null) { NewExists = false });
                var targetRole = target.Profile ? "profile" : "character";
                var movedId = entry.Id.Contains(':') ? entry.Id : role + ":" + entry.Id;
                changes.Add(new(targetRole + "/" + targetList + "/" + movedId, "Receive " + movedId,
                    FieldValue.Null, FieldValue.Of(Convert.ToBase64String(entry.Serial))) { OldExists = false });
            }
            if (entries.Length > 0)
            {
                var sourceCount = source.Root.All(sourceList).Count();
                var targetCount = target.Root.All(targetList).Count();
                changes.Add(new(role + "/" + sourceList + "/count", "Source item count", FieldValue.Of(sourceCount), FieldValue.Of(sourceCount - entries.Length)) { Derived = true });
                changes.Add(new((target.Profile ? "profile" : "character") + "/" + targetList + "/count", "Destination item count", FieldValue.Of(targetCount), FieldValue.Of(targetCount + entries.Length)) { Derived = true });
                if (!source.Profile)
                    foreach (var (slot, index) in source.Root.Children("equipped_inventory_list").Select((s, i) => (s, i)))
                    {
                        var old = slot.Integer("inventory_list_index");
                        var updated = entries.Any(e => e.Index == old) ? -1 : old - entries.Count(e => e.Index < old);
                        if (updated != old) changes.Add(new($"character/slots/{index}/index", "Rebase / unequip slot " + index, FieldValue.Of(old), FieldValue.Of(updated)) { Derived = true });
                    }
            }
            return new() { Changes = changes, Warnings = [SerialWrites.Warning,
                "Character and profile are staged/exported together. Matching imported generations do not prove gameplay compatibility. Cloud mirrors are preserved; the game may overwrite changes on load.",
                "Byte-only banks/lost loot have no flags or pickup order. Those known UI fields are dropped on deposit and recreated on withdrawal. Skin/development/unknown metadata or unknown flag bits that cannot fit are rejected. Destination capacity uses a conservative SDU policy."] };
        }
        catch (SaveFormatException ex) { return new() { Errors = [new("", ex.Message)], Warnings = [SerialWrites.Warning] }; }
    }

    public void Apply(string actionId, ActionScope scope)
    {
        var (source, target, sourceList, targetList, role) = Route(actionId);
        foreach (var entry in Select(source, sourceList, role, scope))
        {
            target.Inventory.Append(targetList, entry.Field, entry.Id.Contains(':') ? entry.Id : role + ":" + entry.Id);
            source.Inventory.Remove(entry.Id);
        }
        source.InvalidateItems();
        target.InvalidateItems();
    }

    private (GearboxDocument Source, GearboxDocument Target, string SourceList, string TargetList, string Role) Route(string actionId) => actionId switch
    {
        "transfer/deposit" => (group.Character, group.Profile, GearboxInventory.Backpack, GearboxInventory.Bank, "character"),
        "transfer/withdraw" => (group.Profile, group.Character, GearboxInventory.Bank, GearboxInventory.Backpack, "profile"),
        "transfer/claim" => (group.Profile, group.Character, GearboxInventory.LostLoot, GearboxInventory.Backpack, "profile"),
        _ => throw new SaveFormatException("Unknown group transfer action."),
    };

    private static InventoryEntry[] Select(GearboxDocument source, string list, string role, ActionScope scope)
    {
        if (scope.RowKeys is null) throw new SaveFormatException("Select explicit source-container rows before transferring.");
        var prefix = role + "/" + list + "/";
        if (scope.QueryScope is { } query && query.CollectionId != role + "/" + list)
            throw new SaveFormatException("The query belongs to a different source container.");
        var entries = source.Inventory.Entries.Where(e => e.List == list).ToArray();
        var keys = scope.RowKeys.ToHashSet(StringComparer.Ordinal);
        var known = entries.Select(entry => prefix + entry.Id).ToHashSet(StringComparer.Ordinal);
        if (keys.Count != scope.RowKeys.Count || !keys.IsSubsetOf(known))
            throw new SaveFormatException("The source selection is stale or belongs to another container.");
        return entries.Where(e => keys.Contains(prefix + e.Id)).ToArray();
    }
}
