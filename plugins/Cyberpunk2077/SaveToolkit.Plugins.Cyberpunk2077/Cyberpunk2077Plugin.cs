using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Cyberpunk2077;

public sealed class Cyberpunk2077Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("cyberpunk2077", "Cyberpunk 2077", "0.6.0")
    {
        Description = "Experimental PS5 stored-value edits and planned stack actions; equipment/world inspection, stored-perk graphs and link-first travel maps.",
        Games = [new GameTitle("PPSA04027", "Cyberpunk 2077")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/WolvenKit/CyberCAT", "https://github.com/WolvenKit/WolvenKit/tree/main/WolvenKit.RED4/Save",
            "https://github.com/Deweh/CyberCAT-SimpleGUI",
            CyberpunkReferences.Upstream + "/items.bin", CyberpunkReferences.Upstream + "/CP2077SaveEditor/Resources/ItemClasses.json",
            CyberpunkReferences.Upstream + "/CP2077SaveEditor/Resources/Facts.json",
            CyberpunkReferences.Upstream + "/CP2077SaveEditor/Resources/Vehicles.json",
            CyberpunkReferences.Upstream + "/CP2077SaveEditor/Resources/FastTravel.json",
            ScriptableReader.SourceUrl,
            TravelMapInspection.SourceUrl,
            "https://www.cyberpunk.net/en/build-planner",
            "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SNewPerk.cs",
            "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SAttributeData.cs",
            "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Types/Classes/SProficiency.cs",
            "https://github.com/PixelRick/CyberpunkSaveEditor/blob/3afe7428e9f26165e371ea7a3ee2cfd4ee4f1062/source/redx/csav/nodes/questSystem/FactsDB/FactsTable.h",
            "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Helper/InventoryHelper.cs",
            "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Classes/ItemData.cs",
            "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/ModifiersBufferReader.cs",
            "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/PersistencySystem2Parser.cs",
            "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/JournalManagerParser.cs",
            "https://cyberpunk.fandom.com/wiki/Bounce_Back?oldid=297811", "https://cyberpunk.fandom.com/wiki/MaxDoc?oldid=297812",
            "https://cyberpunk.fandom.com/wiki/F-GX_Frag_Grenade?oldid=301113", "https://cyberpunk.fandom.com/wiki/EMP_Grenade?oldid=301118",
            "https://cyberpunk.fandom.com/wiki/X-22_Flashbang_Grenade?oldid=287501",
            "https://cyberpunk.fandom.com/wiki/Constitutional_Arms_Unity?oldid=290048",
            "https://community.fandom.com/wiki/Help:Licensing?oldid=3983537"],
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
