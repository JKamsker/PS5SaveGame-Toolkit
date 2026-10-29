namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Only observed default records and researched unoccupied socket layouts are hidden.</summary>
internal static class AbilitySlots
{
    public const string EmptyLabel = "Empty slot";
    public const string ScriptSource = "https://github.com/Lknechtli/BetterCombatEvolved/blob/" +
        "a85e52c3e3841863999b45afc2a0fd1f284a6bc0/Witcher3/content/scripts/game/gameplay/ability/PlayerAbilityManager.ws";
    public const string Source = "Inferred occupancy. Source: native PS5 tagged records; cahian save converter (MIT), " +
        "https://github.com/cahian/witcher3-remastered-to-nextgen-save-converter/tree/7ae8b6fe71c4b36e571246ad25629d03c7ec8219/w3save ; " +
        "CD PROJEKT RED PlayerAbilityManager, mirrored by Lknechtli/BetterCombatEvolved: " + ScriptSource + ". " +
        "Omitted socketedSkill/item is the observed default empty socket; unknown layouts remain visible.";

    public static bool IsEmpty(string array, TaggedRecord record, IReadOnlyList<string> names) => array switch {
        "skills" => record.Properties.Count == 0,
        "skillSlots" => EmptySkillSocket(record, names),
        "mutagenSlots" => record.Find("equipmentSlot") is { Type: "EEquipmentSlots", Value.Length: sizeof(ushort) } &&
            record.Find("item") is null && record.Properties.All(p => KnownProperty(p) && p.Name is "equipmentSlot" or "skillGroupID" or "unlockedAtLevel"),
        _ => false,
    };

    private static bool EmptySkillSocket(TaggedRecord record, IReadOnlyList<string> names)
    {
        if (record.Find("id") is not { Type: "Int32", Value.Length: sizeof(int) } ||
            record.Properties.Any(p => !KnownProperty(p) || p.Name is not ("id" or "unlocked" or "groupID" or "unlockedOnLevel" or "socketedSkill")))
        {
            return false;
        }

        return record.Find("socketedSkill") is not { } socket || socket is { Type: "ESkill", Value.Length: sizeof(ushort) } &&
            AbilityReader.NameValue(socket, names) == "S_SUndefined";
    }

    private static bool KnownProperty(TaggedProperty property) => property.Name switch {
        "id" or "groupID" or "unlockedOnLevel" or "skillGroupID" or "unlockedAtLevel" =>
            property is { Type: "Int32", Value.Length: sizeof(int) },
        "unlocked" => property is { Type: "Bool", Value.Length: 1 } && property.Value[0] <= 1,
        "socketedSkill" => property is { Type: "ESkill", Value.Length: sizeof(ushort) },
        "equipmentSlot" => property is { Type: "EEquipmentSlots", Value.Length: sizeof(ushort) },
        _ => false,
    };
}
