using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.EldenRing;

public sealed class EldenRingPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("eldenring", "Elden Ring", "0.1.0")
    {
        Description = "PlayStation memory.dat character profiles, attributes and bounded rune editing.",
        Games =
        [
            new GameTitle("PPSA03169", "Elden Ring") { Region = "JP" },
            new GameTitle("PPSA04608", "Elden Ring") { Region = "Asia" },
            new GameTitle("PPSA04609", "Elden Ring") { Region = "EU" },
            new GameTitle("PPSA04610", "Elden Ring") { Region = "US" },
        ],
        Maturity = PluginMaturity.Experimental,
        References =
        [
            "https://github.com/ClayAmore/ER-Save-Editor",
            "https://github.com/Hapfel1/er-save-manager",
            "https://soulsmodding.com/doku.php?id=format:sl2",
            "https://prosperopatches.com/PPSA03169",
            "https://prosperopatches.com/PPSA04608",
            "https://prosperopatches.com/PPSA04609",
            "https://prosperopatches.com/PPSA04610",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (!IsTargetFile(file) || file.Length < EldenRingLayout.MinimumFileSize)
        {
            return ProbeResult.None;
        }

        return EldenRingParser.HasHeader(file.ReadHeader(EldenRingLayout.HeaderSize))
            ? ProbeResult.Match(ProbeScore.Title, "Elden Ring character slots and profile")
            : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file)
    {
        if (!IsTargetFile(file))
        {
            throw new SaveFormatException("Expected an Elden Ring PS5 memory.dat file from a supported retail title.");
        }

        var bytes = file.ReadAllBytes();
        return new EldenRingDocument(file.RelativePath, bytes, EldenRingParser.Parse(bytes));
    }

    private bool IsTargetFile(ISaveFile file) =>
        ((string.Equals(file.PathInDirectory, "memory.dat", StringComparison.OrdinalIgnoreCase)
            && string.Equals(file.Directory.Name, "sce_sdmemory", StringComparison.OrdinalIgnoreCase))
            || string.Equals(file.PathInDirectory, "sce_sdmemory/memory.dat", StringComparison.OrdinalIgnoreCase))
        && Info.Games.Any(game => string.Equals(game.TitleId, file.Directory.TitleId, StringComparison.OrdinalIgnoreCase));
}
