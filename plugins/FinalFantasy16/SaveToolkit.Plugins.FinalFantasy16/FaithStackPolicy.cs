namespace SaveToolkit.Plugins.FinalFantasy16;

// This documents the inherited experimental writer. Names/categories never authorize new caps or edits.
internal static class FaithStackPolicy
{
    private const string LegacyPolicy = "Legacy experimental policy: only this existing main-bank signed 32-bit stack is patched. " +
        "0..2147483647 is a storage range, not a gameplay cap. Console load/save/reload is untested. ";

    public static string Warning(int id, string category) => id switch
    {
        1 => LegacyPolicy + "Gil purchase/spend behavior and its effective cap are unverified.",
        2 => LegacyPolicy + "Renown is separate from reward-claim state; changing this stack does not claim or reset rewards.",
        3 => LegacyPolicy + "AP is separate from learned ranks, costs and refund accounting; no skill state is adjusted.",
        _ => category switch
        {
            "consumable" => LegacyPolicy + "The active bank, UseItem capacity and pouch upgrades are unverified; explicit targets are not a legal max or calculated refill.",
            "quest-item" or "curiosity" or "orchestrion" => LegacyPolicy + "This label does not prove quest, upgrade, collection or unlock state. No dependent flags are changed.",
            "weapon" or "belt" or "vambrace" or "accessory" => LegacyPolicy + "Equipment banks, equipped references, ownership and uniqueness limits are not reconciled.",
            _ => LegacyPolicy + "Game-specific limits and progression effects are unknown. Large values may be clamped or rejected by the game.",
        },
    };
}
