using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record ItemIdentity(ulong TweakId, uint Seed, ushort Counter, byte Flags)
{
    public const int WireSize = 16;
    private const int SeedOffset = 8;
    private const int CounterOffset = 13;
    private const int FlagsOffset = 15;
    public static ItemIdentity Read(ReadOnlySpan<byte> bytes) => new(BinaryData.UInt64(bytes, 0), BinaryData.UInt32(bytes, SeedOffset),
        RedPackage.UInt16(bytes, CounterOffset), BinaryData.Slice(bytes, FlagsOffset, 1)[0]);
    public static ItemIdentity FromInventory(InventoryItem item, CsavContainer container, CsavTree tree) =>
        Read(container.Payload.AsSpan(tree.Nodes[item.NodeId].Offset + sizeof(int), WireSize));
    public override string ToString() => $"0x{TweakId:X16} / seed {Seed} / counter {Counter} / flags 0x{Flags:X2}";
}

internal sealed record EquipmentSlot(string Area, int Index, bool Active, ItemIdentity? Item, InventoryItem? Inventory,
    string Match, ulong SlotId, bool HasPrerequisite)
{
    public string Key => FormattableString.Invariant($"equipment/{Area}/{Index}");
    public bool Cyberware => Area.EndsWith("CW", StringComparison.Ordinal);
    // Index -1 is an omitted slot array, not a vacant stored slot.
    public bool IsEmpty => Index >= 0 && Item is null;
}

internal sealed class EquipmentData
{
    private readonly IReadOnlyDictionary<int, string> _states;
    private EquipmentData(IReadOnlyList<EquipmentSlot> slots)
    {
        Slots = slots;
        _states = slots.Where(slot => slot.Inventory is not null).GroupBy(slot => slot.Inventory!.NodeId)
            .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(slot => $"{slot.Area}/{slot.Index}")));
    }
    public IReadOnlyList<EquipmentSlot> Slots { get; }
    public string State(int nodeId) => _states.GetValueOrDefault(nodeId, "Not listed in decoded owner-1 loadout");

    public static EquipmentData Read(ScriptableReader reader, CsavContainer container, CsavTree tree, IReadOnlyList<InventoryItem> items)
    {
        var player = reader.Player("EquipmentSystem", "ownerData", "EquipmentSystemPlayerData");
        var loadout = reader.Nested(player, "equipment", "gameSLoadout");
        var areas = reader.Array(loadout, "equipAreas", "gameSEquipArea");
        var inventory = items.Where(item => item.ContainerId == 1)
            .ToLookup(item => ItemIdentity.FromInventory(item, container, tree));
        var result = new List<EquipmentSlot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in areas)
        {
            var name = area.Enum("areaType", "gamedataEquipmentArea", "Invalid");
            if (!seen.Add(name)) throw new SaveFormatException("Ambiguous duplicate equipment area.");
            var slots = reader.Array(area, "equipSlots", "gameSEquipSlot");
            for (var index = 0; index < slots.Count; index++)
            {
                var slot = slots[index];
                ItemIdentity? identity = null;
                if (slot.Find("itemID") is not null)
                {
                    var id = reader.Nested(slot, "itemID", "gameItemID");
                    identity = new ItemIdentity(ScriptableReader.UInt64(id, "id", "TweakDBID"), ScriptableReader.UInt32(id, "rngSeed"),
                        ScriptableReader.UInt16(id, "uniqueCounter"), ScriptableReader.Byte(id, "flags", "Uint8"));
                    if (identity.TweakId == 0) identity = null;
                }
                // Structure belongs to the wire record, not gameItemID. All four gameplay identity members must match.
                var matches = identity is null ? [] : inventory[identity].ToArray();
                result.Add(new EquipmentSlot(name, index, area.Int("activeIndex") == index, identity,
                    matches.Length == 1 ? matches[0] : null,
                    identity is null ? "Empty stored slot" : matches.Length switch
                    { 1 => "Exact owner-1 instance match", 0 => "Unresolved; no matching owner-1 instance", _ => "Ambiguous; multiple matching instances" },
                    ScriptableReader.UInt64(slot, "slotID", "TweakDBID"), slot.Find("unlockPrereq") is not null));
            }
            // Some wheel areas omit equipSlots entirely. Preserve that distinction in the inspection.
            if (slots.Count == 0) result.Add(new EquipmentSlot(name, -1, false, null, null, "No stored slot array", 0, false));
        }
        return new EquipmentData(result);
    }
}
