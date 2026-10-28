using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Witcher3;

public sealed class Witcher3Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("witcher3", "The Witcher 3: Wild Hunt - Complete Edition", "0.4.0")
    {
        Description = "Attributed item/ability/Gwent catalogs, read-only recipes/journal/facts, and Experimental preserving progression/quantity edits.",
        Games = [new GameTitle("PPSA04018", "The Witcher 3: Wild Hunt - Complete Edition")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/Atvaark/W3SavegameEditor", "https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md",
            "https://github.com/cahian/witcher3-remastered-to-nextgen-save-converter",
            "https://witcher.fandom.com/wiki/Category:The_Witcher_3_items", "https://www.fandom.com/licensing",
            "https://witcher.fandom.com/wiki/The_Witcher_3_abilities_(obsolete)/Combat?oldid=649876",
            "https://witcher.fandom.com/wiki/The_Witcher_3_abilities_(obsolete)/Signs?oldid=649877",
            "https://witcher.fandom.com/wiki/The_Witcher_3_abilities_(obsolete)/Alchemy?oldid=649878",
            "https://witcher.fandom.com/wiki/The_Witcher_3_abilities_(obsolete)/General?oldid=649879",
            "https://witcher.fandom.com/wiki/Mutations_(Blood_and_Wine)?oldid=617694",
            "https://github.com/vishalgupta-git/GwentTracker/tree/d8eab8c25c409be42a0ea7b43b901a43dfefed18/data",
            "https://github.com/vishalgupta-git/GwentTracker/blob/d8eab8c25c409be42a0ea7b43b901a43dfefed18/src/lib/save/parse.ts",
            "https://github.com/dodojesuslol/w3-quest-tracker/blob/6078f8f64a1ede3a304452bfe3cff710ae1d554e/docs/save-format.md",
            "https://github.com/aschleg/witcher-3-enemy-scaling-and-other-changes/blob/66684cbe57eb20286f9ce65e551a4a0313543592/mod/gameplay/abilities/geralt_skills.xml"],
    };

    public ProbeResult Probe(ISaveFile file) => file.Directory.TitleId == "PPSA04018" &&
        file.FileName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) && file.ReadHeader(8).AsSpan().SequenceEqual("SNFHFZLC"u8)
        ? ProbeResult.Match(ProbeScore.Title, "The Witcher 3 gameplay save") : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (file.Length > BinaryData.MaxFileSize)
        {
            throw new SaveFormatException("Witcher save exceeds the supported 128 MiB input limit.");
        }

        var container = WitcherContainer.Parse(file.ReadAllBytes());
        var directory = Sav3Directory.Parse(container);
        return new WitcherDocument(file.RelativePath, container, directory, Sav3Tree.Parse(container.Payload, directory), references);
    }
}
