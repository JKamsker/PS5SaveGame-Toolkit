using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Witcher3;

public sealed class Witcher3Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("witcher3", "The Witcher 3: Wild Hunt - Complete Edition", "0.2.0")
    {
        Description = "Experimental player progression, Crowns and inventory editing with a preserving SAV3/SNFH writer.",
        Games = [new GameTitle("PPSA04018", "The Witcher 3: Wild Hunt - Complete Edition")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/Atvaark/W3SavegameEditor", "https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md",
            "https://github.com/cahian/witcher3-remastered-to-nextgen-save-converter"],
    };

    public ProbeResult Probe(ISaveFile file) => file.Directory.TitleId == "PPSA04018" &&
        file.FileName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) && file.ReadHeader(8).AsSpan().SequenceEqual("SNFHFZLC"u8)
        ? ProbeResult.Match(ProbeScore.Title, "The Witcher 3 gameplay save") : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file)
    {
        if (file.Length > BinaryData.MaxFileSize)
        {
            throw new SaveFormatException("Witcher save exceeds the supported 128 MiB input limit.");
        }

        var container = WitcherContainer.Parse(file.ReadAllBytes());
        var directory = Sav3Directory.Parse(container);
        return new WitcherDocument(file.RelativePath, container, directory, Sav3Tree.Parse(container.Payload, directory));
    }
}
