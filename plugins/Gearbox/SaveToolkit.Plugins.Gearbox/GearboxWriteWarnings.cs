namespace SaveToolkit.Plugins.Gearbox;

internal static class GearboxWriteWarnings
{
    private const string ConsoleNotice = "Not validated on console.";
    public const string ConsoleValidation = ConsoleNotice + " The game may reject or overwrite the edited save.";

    public const string SchemaEvidence = "Verified schema meaning from the cited protobuf definitions and upstream editor tables; " +
        "matching layouts and available values were checked against PS5 samples. This does not establish console acceptance. " +
        "Sources: CJ Kucera / Abram Hindle (zlib; schemas credit Rick/Gibbed), retrieved 2026-10-28: " +
        "https://github.com/apocalyptech/bl3-cli-saveedit/tree/d459b28a7e88f1a5ae0f16118272d8a5383b8b04 and " +
        "https://github.com/apocalyptech/ttwl-cli-saveedit/tree/1fde90dc43e7f7ee261413d85c366e0a7236dd9b.";

    public static string ForWrite(string? warning)
    {
        if (string.IsNullOrEmpty(warning)) return ConsoleValidation;
        return warning.Contains(ConsoleNotice, StringComparison.Ordinal) ? warning : ConsoleValidation + " " + warning;
    }
}
