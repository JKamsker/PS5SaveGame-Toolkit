using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.EldenRing;

public sealed class EldenRingPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("eldenring", "Elden Ring", "0.4.0")
    {
        Description = "Experimental mirrored names, bounded runes/history and ordinary-stack edits with F6 refill/review; attributed inventory and equipment browsing.",
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
            "https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df",
            "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/weapon_name.rs",
            "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/item_name.rs",
            "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/armor_name.rs",
            "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/accessory_name.rs",
            "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/db/aow_name.rs",
            "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/vm/equipment.rs",
            "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs",
            "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/playstation/user_data_10.rs",
            "https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/14",
            "https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31",
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

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (!IsTargetFile(file))
        {
            throw new SaveFormatException("Expected an Elden Ring PS5 memory.dat file from a supported retail title.");
        }

        var bytes = file.ReadAllBytes();
        return new EldenRingDocument(file.RelativePath, bytes, EldenRingParser.Parse(bytes), references);
    }

    private bool IsTargetFile(ISaveFile file) =>
        ((string.Equals(file.PathInDirectory, "memory.dat", StringComparison.OrdinalIgnoreCase)
            && string.Equals(file.Directory.Name, "sce_sdmemory", StringComparison.OrdinalIgnoreCase))
            || string.Equals(file.PathInDirectory, "sce_sdmemory/memory.dat", StringComparison.OrdinalIgnoreCase))
        && Info.Games.Any(game => string.Equals(game.TitleId, file.Directory.TitleId, StringComparison.OrdinalIgnoreCase));
}
