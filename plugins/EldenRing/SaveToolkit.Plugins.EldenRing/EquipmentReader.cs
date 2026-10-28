namespace SaveToolkit.Plugins.EldenRing;

internal sealed record EquipmentLink(string Key, string Label, uint Handle, uint Position,
    uint? AssemblyId, uint EquippedId, InventoryEntry? Entry, string Status);

// Read-only relationship checks, not a complete list of bytes needed by an equip transaction.
// ClayAmore 014107f0: save/common/save_slot.rs and vm/equipment.rs (MIT).
internal static class EquipmentReader
{
    private const int AssemblyPrefixWords = 7;
    private const int QuickSlotCount = 10;
    private const int PouchSlotCount = 6;
    private const int QuickActiveBytes = sizeof(uint);
    private const int EquippedQuickWord = 22;
    private const int EquippedPouchWord = 32;
    private const int ItemPairSize = 2 * sizeof(uint);
    private const int HandSlotCount = 6;
    private const int WeaponSlotCount = 10;
    private const int HeadWord = 12;
    private const int ChestWord = 13;
    private const int ArmsWord = 14;
    private const int LegsWord = 15;
    private const int FirstTalismanWord = 17;
    private const int LastTalismanWord = 20;

    public static IReadOnlyList<EquipmentLink> Read(byte[] bytes, SemanticCharacter character)
    {
        var ranges = character.Ranges.ToDictionary(range => range.Name, StringComparer.Ordinal);
        var held = character.Containers.Single(container => container.Id == "held");
        var rows = new List<EquipmentLink>();
        for (var word = 0; word < SemanticLayout.EquipmentSize / sizeof(uint); word++)
        {
            var label = Label(word);
            if (label is null) continue; // Unknown words are retained in the independent raw tables.
            rows.Add(Link($"gear/{word}", label,
                ReadWord(bytes, ranges["ChrAsm2"], word), ReadWord(bytes, ranges["EquipData"], word),
                ReadWord(bytes, ranges["ChrAsm"], word + AssemblyPrefixWords),
                ReadWord(bytes, ranges["EquippedItems"], word), held,
                word < WeaponSlotCount ? "weapon" : word <= LegsWord ? "armor" : "talisman"));
        }
        var quick = ranges["QuickItems"];
        for (var index = 0; index < QuickSlotCount + PouchSlotCount; index++)
        {
            var pouch = index >= QuickSlotCount;
            var slot = pouch ? index - QuickSlotCount : index;
            var pairOffset = index * ItemPairSize + (pouch ? QuickActiveBytes : 0);
            var handle = EldenRingParser.ReadUInt32(bytes, quick.Offset + pairOffset);
            var position = EldenRingParser.ReadUInt32(bytes, quick.Offset + pairOffset + sizeof(uint));
            var equipped = ReadWord(bytes, ranges["EquippedItems"], (pouch ? EquippedPouchWord : EquippedQuickWord) + slot);
            rows.Add(Link($"{(pouch ? "pouch" : "quick")}/{slot}", $"{(pouch ? "Pouch" : "Quick item")} {slot + 1}",
                handle, position, null, equipped, held, "goods"));
        }
        return rows;
    }

    private static EquipmentLink Link(string key, string label, uint handle, uint position, uint? assembly,
        uint equipped, InventoryContainer held, string category)
    {
        EquipmentLink Result(InventoryEntry? entry, string status) => new(key, label, handle, position, assembly, equipped, entry, status);
        if (handle == 0)
            return Result(null, "Unassigned candidate; empty/unarmed rules unverified");
        var matches = held.Entries.Where(entry => entry.Group == "common" && entry.Handle == handle).ToArray();
        if (matches.Length != 1)
            return Result(null, matches.Length == 0 ? "No held common-row match" : "Ambiguous held handle; no instance selected");
        var entry = matches[0];
        if (entry.ItemId is not { } id || entry.Category != category || entry.Quantity == 0)
            return Result(entry, "Unresolved identity, category or zero quantity; protected");
        // EquipData uses physical common-array position + 0x180, NOT acquisition/inventoryIndex.
        var expectedPosition = (uint)(entry.Record + SemanticLayout.HeldKeyCapacity);
        var assemblyMatches = assembly is null || assembly == (id & SemanticLayout.IdentityMask);
        return Result(entry, position == expectedPosition && assemblyMatches && equipped == id
            ? "Representations agree in this snapshot; writes disabled"
            : "Representation disagreement; preserved without repair");
    }

    private static uint ReadWord(byte[] bytes, SaveRange range, int word) =>
        EldenRingParser.ReadUInt32(bytes, range.Offset + word * sizeof(uint));

    private static string? Label(int word) => word switch
    {
        < HandSlotCount => $"{(word % 2 == 0 ? "Left" : "Right")} hand {word / 2 + 1}",
        < WeaponSlotCount => $"{(word % 2 == 0 ? "Arrow" : "Bolt")} {(word - HandSlotCount) / 2 + 1}",
        HeadWord => "Head", ChestWord => "Chest", ArmsWord => "Arms", LegsWord => "Legs",
        >= FirstTalismanWord and <= LastTalismanWord => $"Talisman {word - FirstTalismanWord + 1}",
        _ => null,
    };
}
