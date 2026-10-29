using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class EquipmentInspection
{
    public const string CollectionId = "equipment";
    public EquipmentInspection(EquipmentData? data, CyberpunkReferences references, string? warning)
    {
        Warning = warning;
        var slotIds = data?.Slots.Where(slot => slot.Item is not null)
            .ToDictionary(slot => slot.Key, slot => slot.Item!.TweakId) ?? [];
        var rows = data?.Slots.Select(slot =>
        {
            var record = slot.Item is { } item ? references.Item(item.TweakId) : null;
            var name = slot.Item is null ? "Empty" : references.ItemDisplayName(slot.Item.TweakId) ?? $"0x{slot.Item.TweakId:X16}";
            return new InspectionRow(slot.Key, new Dictionary<string, string>
            {
                ["icon"] = "",
                ["area"] = slot.Area, ["slot"] = slot.Index.ToString(CultureInfo.InvariantCulture),
                ["kind"] = slot.Cyberware ? "Cyberware area" : "Equipment area", ["item"] = name,
                ["identity"] = slot.Item?.ToString() ?? "", ["match"] = slot.Match,
                ["node"] = slot.Inventory?.NodeId.ToString(CultureInfo.InvariantCulture) ?? "",
                ["active"] = slot.Active ? "Stored active index" : "",
                ["rules"] = $"Slot ID 0x{slot.SlotId:X16}; prerequisite handle {(slot.HasPrerequisite ? "present (not evaluated)" : "omitted")}. " +
                    "Compatibility, capacity cost/limit, unlock eligibility and bonuses are unknown.",
            }, ScriptableReader.Source + (record is null ? "" : "\n" + references.ItemAttribution(slot.Item!.TweakId)));
        }).ToArray() ?? [];
        Table = new InspectionTable(CollectionId, "Equipment and cyberware (read-only)", [new("icon", "Icon"), new("area", "Stored area"),
            new("slot", "Slot index", IntegerSpec.Int32), new("kind", "Area kind"), new("item", "Item"), new("identity", "Full gameplay identity"),
            new("match", "Inventory join"), new("node", "Inventory node", IntegerSpec.UInt32), new("active", "Active index"), new("rules", "Rules / evidence")], rows,
            "Owner-1 loadout only. All four gameItemID members and player container must match; base IDs alone never resolve equipment. " +
            "Cyberware rows describe stored slots, not a verified capacity or eligibility model. Empty/omitted slots do not establish unlocked slots. " +
            "Special fists/cutscene/wardrobe slots can be listed alongside ordinary equipment; current usability is unknown.",
            cell: (row, column) =>
            {
                if (!slotIds.TryGetValue(row.Key, out var id)) return null;
                return column switch
                {
                    "icon" => references.ItemIcon(id),
                    "item" => references.ItemPresentation(id),
                    _ => null,
                };
            });
    }
    public string? Warning { get; }
    public InspectionTable Table { get; }
    public IReadOnlyList<ViewNode> Build() => [new TextNode(Warning ?? Table.Metadata.Description!) { Tone = Warning is null ? Tone.Info : Tone.Warning }, Table.View()];
}
