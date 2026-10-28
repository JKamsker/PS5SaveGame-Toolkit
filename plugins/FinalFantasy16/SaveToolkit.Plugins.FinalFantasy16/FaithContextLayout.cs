namespace SaveToolkit.Plugins.FinalFantasy16;

// Supported MIT template layouts. Keep readers and fixed-width writers on the same offsets.
internal static class FaithContextLayout
{
    public const int EquipmentBanks = 3;
    public const int EquipmentSlots = 6;
    public const int EquipmentBankSize = EquipmentSlots * sizeof(int);
    public const int SkillRanks = 68;
    public const int FlagBytes = 12;
    public const int ProtectOffset = SkillRanks + FlagBytes;
    public const int MapOffset = 4;
    public const int PartyIdOffset = 16;
    public const int PartyCountOffset = 20;
    public const int ChildLevelOffset = 8;
    public const int ChildXpOffset = 12;
    public const int HpOffset = 16;
    public const int RecoveryOffset = 20;
    public const int LimitBreakOffset = 24;
    public const int CurrentSummonOffset = 28;
    public const int LearnedSummonsOffset = 32;
    public const int RevealedSummonsOffset = 36;
    public const int SummonModesOffset = 40;
    public const int SummonCount = 3;
    public const int CommandsOffset = 52;
    public const int CommandCount = 22;
    public const int ShortcutOffset = 140;
    public const int ShortcutCount = 4;
}
