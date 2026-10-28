using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Cyberpunk2077;

public sealed class Cyberpunk2077Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("cyberpunk2077", "Cyberpunk 2077", "0.3.0")
    {
        Description = "CSAV node, inventory and fact explorer; experimental attributes, development points and stack editing for the verified PS5 schema.",
        Games = [new GameTitle("PPSA04027", "Cyberpunk 2077")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/WolvenKit/CyberCAT", "https://github.com/WolvenKit/WolvenKit/tree/main/WolvenKit.RED4/Save",
            "https://github.com/Deweh/CyberCAT-SimpleGUI",
            CyberpunkReferences.Upstream + "/items.bin", CyberpunkReferences.Upstream + "/CP2077SaveEditor/Resources/ItemClasses.json",
            CyberpunkReferences.Upstream + "/CP2077SaveEditor/Resources/Facts.json",
            CyberpunkReferences.Upstream + "/CP2077SaveEditor/Resources/Vehicles.json",
            CyberpunkReferences.Upstream + "/CP2077SaveEditor/Resources/FastTravel.json"],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        var header = file.ReadHeader(29);
        return file.Directory.TitleId == "PPSA04027" && file.FileName.Equals("sav.dat", StringComparison.OrdinalIgnoreCase) &&
            header.Length >= 29 && header.AsSpan(0, 4).SequenceEqual("VASC"u8) && header.AsSpan(25, 4).SequenceEqual("FZLC"u8)
            ? ProbeResult.Match(ProbeScore.Title, "Cyberpunk 2077 gameplay save") : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (file.Length > BinaryData.MaxFileSize)
        {
            throw new SaveFormatException("Cyberpunk save exceeds the supported 128 MiB input limit.");
        }

        var container = CsavContainer.Parse(file.ReadAllBytes());
        var tree = CsavTree.Parse(container);
        return new CyberpunkDocument(file.RelativePath, container, tree, InventoryReader.Read(container, tree), references);
    }
}
