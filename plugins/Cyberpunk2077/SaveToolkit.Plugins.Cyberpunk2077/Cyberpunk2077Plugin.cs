using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Cyberpunk2077;

public sealed class Cyberpunk2077Plugin : IGamePlugin, ISaveDecoderProvider
{
    public PluginInfo Info { get; } = new("cyberpunk2077", "Cyberpunk 2077", "0.7.0")
    {
        Description = "Experimental PS5 stored-value edits; attributed item/cyberware icons, planner descriptions, stored-perk graphs and Night City reference art.",
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
            "https://www.cyberpunk.net/build/8603-b6e6f72c476c734a52a4.js",
            "https://cyberpunk.fandom.com/wiki/Category:Cyberpunk_2077_images_-_Inventory_Icons",
            "https://cyberpunk.fandom.com/wiki/Biomonitor?oldid=280059",
            "https://cyberpunk.fandom.com/wiki/Blood_Pump?oldid=280138",
            "https://cyberpunk.fandom.com/wiki/Gorilla_Arms?oldid=307846",
            "https://cyberpunk.fandom.com/wiki/Mantis_Blades?oldid=307995",
            "https://cyberpunk.fandom.com/wiki/Monowire?oldid=307954",
            "https://cyberpunk.fandom.com/wiki/Projectile_Launch_System?oldid=307975",
            "https://cyberpunk.fandom.com/wiki/Reinforced_Tendons?oldid=300690",
            "https://cyberpunk.fandom.com/wiki/Second_Heart?oldid=285680",
            "https://cyberpunk.fandom.com/wiki/Smart_Link?oldid=278451",
            "https://cyberpunk.fandom.com/wiki/Subdermal_Armor?oldid=286231",
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

    public IReadOnlyList<ISaveDecoderHook> Decoders => [new CyberpunkEvidenceDecoder()];

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
