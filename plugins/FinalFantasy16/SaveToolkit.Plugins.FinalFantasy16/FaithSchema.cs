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
}
