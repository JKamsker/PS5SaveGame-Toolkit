namespace SaveToolkit.Plugins.FinalFantasy16;

// Match individual blocks, not total file sizes: retail contains old demo-era saves too.
internal sealed record FaithSchema(string Name, bool IsDemo)
{
    public const uint PlayerType = 1;
    public const uint ItemType = 6;
    public const uint UserType = 7;
    public const ulong PlayerVersion = 202211161;
    public const ulong ItemVersion = 2021110401;
    public const ulong UserVersion = 2021061001;
    public const int LegacyPlayerLength = 272;
    public const int ExtendedPlayerLength = 1536;
    public const int ItemLength = 36448;
    public const int UserLength = 104;
    public const uint PartyType = 2;
    public const uint EquipmentType = 13;
    public const uint SkillType = 27;
    public const ulong PartyVersion = 2021112401;
    public const ulong EquipmentVersion = 20220104;
    public const ulong SkillVersion = 20210422;
    public const int PartyLength = 168;
    public const int EquipmentLength = 72;
    public const int SkillLength = 96;

    public static FaithSchema ForTitle(string title) => title switch
    {
        "PPSA10665" => new("Retail PS5", false),
        "PPSA13803" => new("Demo PS5", true),
        _ => throw new ArgumentException("Unsupported title.", nameof(title)),
    };

    public bool SupportsPlayer(FaithBlock block) => block.Type == PlayerType && block.Version == PlayerVersion &&
        (block.PayloadLength == LegacyPlayerLength || (!IsDemo && block.PayloadLength == ExtendedPlayerLength));

    public static bool SupportsItems(FaithBlock block) => block.Type == ItemType &&
        block.Version == ItemVersion && block.PayloadLength == ItemLength;

    public static bool SupportsUser(FaithBlock block) => block.Type == UserType &&
        block.Version == UserVersion && block.PayloadLength == UserLength;

    public static bool SupportsParty(FaithBlock block) => block.Type == PartyType &&
        block.Version == PartyVersion && block.PayloadLength == PartyLength;

    public static bool SupportsEquipment(FaithBlock block) => block.Type == EquipmentType &&
        block.Version == EquipmentVersion && block.PayloadLength == EquipmentLength;

    public static bool SupportsSkills(FaithBlock block) => block.Type == SkillType &&
        block.Version == SkillVersion && block.PayloadLength == SkillLength;

    // A duplicate of any version makes the semantic owner ambiguous. Never select just the
    // recognized occurrence when another occurrence with the same type is present.
    public static FaithBlock? Unique(FaithContainer container, uint type, Func<FaithBlock, bool> supported)
    {
        var blocks = container.Blocks.Where(block => block.Type == type).Take(2).ToArray();
        return blocks.Length == 1 && supported(blocks[0]) ? blocks[0] : null;
    }
}
