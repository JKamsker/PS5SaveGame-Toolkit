using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Insomniac;

public sealed class InsomniacPlugin : IGamePlugin, ISaveDecoderProvider
{
    public IReadOnlyList<ISaveDecoderHook> Decoders { get; } = [new RiftEvidenceDecoder()];

    public PluginInfo Info { get; } = new("insomniac", "Ratchet & Clank: Rift Apart", "0.8.0")
    {
        Description = "Experimental bolt, gold-bolt option and boolean preference edits, with read-only arsenal/world inspection. " + RiftScalarWrites.Warning,
        Games = [new GameTitle("PPSA01474", "Ratchet & Clank: Rift Apart")],
        Maturity = PluginMaturity.Experimental,
        References =
        [
            RiftCollectibleMap.GuideUrl,
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/corson-v/",
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/sargasso/",
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/scarstu-debris-field/",
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/savali/",
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/blizar-prime/",
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/torren-iv/",
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/cordelion/",
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/ardolis/",
            "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/viceron/",
            "https://ratchetandclank.fandom.com/wiki/Blackhole_Storm?oldid=401994",
            "https://ratchetandclank.fandom.com/wiki/Bombardier?oldid=401406",
            "https://ratchetandclank.fandom.com/wiki/Bouncer?oldid=402170",
            "https://ratchetandclank.fandom.com/wiki/Burst_Pistol?oldid=401985",
            "https://ratchetandclank.fandom.com/wiki/Buzz_Blades?oldid=402622",
            "https://ratchetandclank.fandom.com/wiki/Cold_Snap?oldid=401999",
            "https://ratchetandclank.fandom.com/wiki/Drillhound?oldid=401409",
            "https://ratchetandclank.fandom.com/wiki/Glove_of_Doom?oldid=402280",
            "https://ratchetandclank.fandom.com/wiki/Headhunter?oldid=401296",
            "https://ratchetandclank.fandom.com/wiki/Lightning_Rod_(Rift_Apart)?oldid=401411",
            "https://ratchetandclank.fandom.com/wiki/Mr._Fungi?oldid=390580",
            "https://ratchetandclank.fandom.com/wiki/Negatron_Collider?oldid=402063",
            "https://ratchetandclank.fandom.com/wiki/Pixelizer?oldid=402326",
            "https://ratchetandclank.fandom.com/wiki/RYNO_8?oldid=400819",
            "https://ratchetandclank.fandom.com/wiki/Ricochet?oldid=402096",
            "https://ratchetandclank.fandom.com/wiki/Rift_Apart_armor?oldid=402364",
            "https://ratchetandclank.fandom.com/wiki/Shatterbomb?oldid=395525",
            "https://ratchetandclank.fandom.com/wiki/The_Enforcer?oldid=403283",
            "https://ratchetandclank.fandom.com/wiki/Topiary_Sprinkler?oldid=395524",
            "https://ratchetandclank.fandom.com/wiki/Void_Repulser?oldid=395549",
            "https://ratchetandclank.fandom.com/wiki/Warmonger?oldid=402415",
            DdlSections.SchemaSource,
            "https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json.license",
            "https://ratchetandclank.fandom.com/wiki/Burst_Pistol",
            "https://ratchetandclank.fandom.com/wiki/Rift_Apart_menu_transcript",
            "https://ratchetandclank.fandom.com/wiki/Warmonger",
            "https://ratchetandclank.fandom.com/wiki/Rift_Apart_menu_transcript?oldid=403465",
            "https://ratchetandclank.fandom.com/wiki/Rift_Apart_armor?oldid=402364",
            "https://www.playstation.com/en-us/games/ratchet-and-clank-rift-apart/",
            "https://ratchetandclank.fandom.com/wiki/Category:Weapons_in_Rift_Apart",
            "https://ratchetandclank.fandom.com/api.php",
            "https://raw.githubusercontent.com/Tkachov/ALERT/bc90ed9b53fbcaa3b7acf94b36f00e77eab0dacb/dat1lib/crc64.py",
            "https://github.com/Tkachov/ALERT/blob/main/dat1lib/types/dat1.py",
            "https://github.com/Tkachov/ALERT/wiki/Formats",
            "https://github.com/VELD-Dev/riftripper",
            "https://ninjasaves.com/cheats/ratchet-clank-rift-apart-ppsa01474",
            "https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet/Data/SaveData.cs",
            "https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet.CLI/Utility/RivetSaveCommand.cs",
            "https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/Rivet.DDL.RCRA/Types/GameCampaignSave.cs",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        try
        {
            var header = file.ReadHeader(Dat1Container.HeaderSize);
            if (!Dat1Container.LooksLike(header, file.Length))
            {
                return ProbeResult.None;
            }

            var title = string.Equals(file.Directory.TitleId, "PPSA01474", StringComparison.OrdinalIgnoreCase);
            var label = file.FileName.Equals("i29-userprefs.save", StringComparison.OrdinalIgnoreCase)
                ? "Rift Apart user preferences (DAT1)"
                : file.FileName.Equals("i29-s.save", StringComparison.OrdinalIgnoreCase)
                    ? "Rift Apart gameplay save (DAT1)"
                    : "Insomniac DAT1 save";
            return ProbeResult.Match(title ? ProbeScore.Title : ProbeScore.Format, label);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return ProbeResult.None;
        }
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        try
        {
            Dat1Container.ValidateFileLength(file.Length);
            return new InsomniacDocument(file.RelativePath, Dat1Container.Parse(file.ReadAllBytes()), file.FileName, references,
                string.Equals(file.Directory.TitleId, "PPSA01474", StringComparison.OrdinalIgnoreCase));
        }
        catch (SaveFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or OverflowException)
        {
            throw new SaveFormatException("The file is not a valid Insomniac DAT1 save.", ex);
        }
    }
}
