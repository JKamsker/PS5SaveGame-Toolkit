using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.AtomicHeart;

public sealed class AtomicHeartPlugin : IGamePlugin
{
    private const int ProbeHeaderBytes = 4096;
    public PluginInfo Info { get; } = new("atomic-heart", "Atomic Heart", "0.1.0")
    {
        Description = "Read-only SaveExtension metadata and Oodle chunk inspector with SHA-1 validation.",
        Games = [new GameTitle("PPSA10695", "Atomic Heart")],
        Maturity = PluginMaturity.ReadOnly,
        References = ["https://github.com/PipeRift/SaveExtension/blob/1.4/Source/SaveExtension/Private/FileAdapter.cpp"],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (file.Length is < 64 or > SaveExtensionFile.MaxBytes) return ProbeResult.None;
        try
        {
            var header = file.ReadHeader(ProbeHeaderBytes).AsSpan();
            return file.Directory.TitleId == "PPSA10695" &&
                header.StartsWith(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0 }) &&
                header.IndexOf("/Script/SaveExtension.SlotInfo\0"u8) >= 0
                ? ProbeResult.Match(ProbeScore.Title, "Atomic Heart slot (SaveExtension)") : ProbeResult.None;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { return ProbeResult.None; }
    }

    public ISaveDocument Open(ISaveFile file)
    {
        if (file.Length > SaveExtensionFile.MaxBytes) throw new SaveFormatException("SaveExtension exceeds the 128 MiB limit.");
        var bytes = file.ReadAllBytes();
        return new AtomicHeartDocument(file.RelativePath, bytes, SaveExtensionFile.Parse(bytes));
    }
}
