using System.Globalization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class CyberwareInspection
{
    public const string CollectionId = "cyberware";
    public CyberwareInspection(IReadOnlyList<InventoryItem> items, EquipmentData? equipment, CyberpunkReferences references,
        CsavContainer container, CsavTree tree)
    {
        var (rows, rowIds) = BuildRows(items, equipment, references, container, tree);
        Table = new InspectionTable(CollectionId, "Cyberware inventory, slots and catalog (read-only)", [new("icon", "Icon"), new("name", "Name / identifier"),
            new("id", "TweakDB ID"), new("ownership", "Inventory evidence"), new("loadout", "Loadout evidence"),
            new("identity", "Instance identity"), new("rules", "Capacity / eligibility")], rows,
            "Separate inventory instances, unresolved slot records and catalog-only records. Classification is community reference data, not proof of entitlement or compatibility. " +
            "Duplicate base records keep distinct inventory keys. Empty slots and absent catalog items never imply capacity, eligibility or available replacements.",
            cell: (row, column) => column switch
            {
                "icon" => references.ItemIcon(rowIds[row.Key]),
                "name" => references.ItemPresentation(rowIds[row.Key]),
                _ => null,
            });
    }
    public InspectionTable Table { get; }
    public IReadOnlyList<ViewNode> Build() => [new TextNode(Table.Metadata.Description!), Table.View()];

    private static (List<InspectionRow> Rows, Dictionary<string, ulong> RowIds) BuildRows(IReadOnlyList<InventoryItem> items,
        EquipmentData? equipment, CyberpunkReferences references, CsavContainer container, CsavTree tree)
    {
        var rows = new List<InspectionRow>();
        var saved = new HashSet<ulong>();
        var rowIds = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var installed = equipment?.Slots.Where(slot => slot.Cyberware && slot.Item is not null).ToArray() ?? [];
        void Add(string key, ulong id, string ownership, string state, string identity)
        {
            rowIds.Add(key, id);
            rows.Add(new InspectionRow(key, new Dictionary<string, string>
            {
                ["icon"] = "",
                ["name"] = references.ItemDisplayName(id) ?? $"0x{id:X16}", ["id"] = $"0x{id:X16}",
                ["ownership"] = ownership, ["loadout"] = state, ["identity"] = identity,
                ["rules"] = "Capacity used/limit, bonus contributors, slot compatibility, unlocks and prerequisites unknown. No replacement or installation writes.",
            }, ScriptableReader.Source + "\n" + references.ItemAttribution(id)));
        }
        foreach (var item in items.Where(item => references.ItemContent(item.TweakId)?.Category == "Cyberware" ||
            references.Item(item.TweakId)?.Category == "Cyberware" ||
            installed.Any(slot => slot.Inventory?.NodeId == item.NodeId)))
        {
            saved.Add(item.TweakId);
            var slots = installed.Where(slot => slot.Inventory?.NodeId == item.NodeId).ToArray();
            Add($"cyberware/inventory/{item.NodeId}", item.TweakId, item.ContainerId == 1 ? "Player-container instance" : $"Container {item.ContainerId}; ownership unknown",
                equipment is null ? "Unknown: loadout unavailable" : slots.Length == 0 ? "Not listed in decoded cyberware slots" :
                    string.Join(", ", slots.Select(slot => $"{slot.Area}/{slot.Index}")),
                ItemIdentity.FromInventory(item, container, tree).ToString());
        }
        foreach (var slot in installed.Where(slot => slot.Inventory is null))
        {
            saved.Add(slot.Item!.TweakId);
            Add("cyberware/" + slot.Key, slot.Item.TweakId, "No unambiguous inventory ownership join",
                $"Stored slot {slot.Area}/{slot.Index}; {slot.Match}", slot.Item.ToString());
        }
        foreach (var record in CyberpunkReferences.All(references.Items).Concat(CyberpunkReferences.All(references.Catalog("item-art")))
            .Where(record => record.Category == "Cyberware").DistinctBy(record => record.Key))
        {
            if (!ulong.TryParse(record.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || saved.Contains(id)) continue;
            saved.Add(id);
            Add("cyberware/catalog/" + record.Key, id, "Catalog only; not observed in classified inventory",
                equipment is null ? "Unknown: loadout unavailable" : "Not listed in decoded cyberware slots", "");
        }
        return (rows, rowIds);
    }
}
