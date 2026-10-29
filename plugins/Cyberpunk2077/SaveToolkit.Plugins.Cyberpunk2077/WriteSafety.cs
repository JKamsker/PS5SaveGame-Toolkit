namespace SaveToolkit.Plugins.Cyberpunk2077;

internal static class WriteSafety
{
    public const string Warning = "Not validated on console. The game may reject or overwrite the edited save. " +
        "CSAV compression, lengths and offsets are repaired; integrity checks in opaque data and console wrappers are unknown. " +
        "Owner validation: https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31.";

    public const string ProgressionWarning = Warning + " Raw stored progression edit, not a level-up transaction. " +
        "Rewards, unlocks, perks and XP curves are not recalculated; the game may ignore or reinterpret this value. " +
        "Bounds are conservative editor policies, not verified gameplay limits.";
}
