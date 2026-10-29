using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.SuckerPunch;

public sealed class SuckerPunchPlugin : IGamePlugin, ISaveDecoderProvider
{
    public IReadOnlyList<ISaveDecoderHook> Decoders => [new SuckerPunchSaveDecoder()];

    public PluginInfo Info { get; } = new("suckerpunch", "Ghost of Tsushima / Ghost of Yōtei", "0.8.0")
    {
        Description = "Experimental Tsushima quantity/play-time editing with action previews and attributed wiki guides; Yōtei inspection.",
        Games =
        [
            new GameTitle("PPSA02225", "Ghost of Tsushima"),
            new GameTitle("PPSA26344", "Ghost of Yōtei"),
        ],
        Maturity = PluginMaturity.Experimental,
        References =
        [
            "https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/main/TECHNICAL.md",
            "https://github.com/KOVRlN/ghostoftsushima-save-converter",
            "https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/Ghost%20of%20Tsushima%20Codes%20%5BSaveWizard%5D.txt",
            "https://github.com/bucanero/apollo-patches/blob/main/PS4/CUSA11456.savepatch",
            "https://github.com/bucanero/apollo-lib/blob/main/source/patches.c",
            "https://ghostfranchise.fandom.com/wiki/Resources?oldid=29221",
            "https://ghostfranchise.fandom.com/wiki/Charms?oldid=32085",
            "https://ghostfranchise.fandom.com/wiki/Techniques?oldid=29271",
            "https://ghostfranchise.fandom.com/wiki/Sword_Kits?oldid=32356",
            "https://ghostfranchise.fandom.com/wiki/Armor?oldid=29366",
            "https://ghostfranchise.fandom.com/wiki/Shinto_Shrines?oldid=29432",
            "https://ghostfranchise.fandom.com/wiki/Arrows?oldid=31796",
            "https://www.powerpyx.com/ghost-of-tsushima-wiki-strategy-guide/",
            .. TsushimaAssetSources.Urls,
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (!file.Directory.TitleId.Equals("PPSA02225", StringComparison.OrdinalIgnoreCase) &&
            !file.Directory.TitleId.Equals("PPSA26344", StringComparison.OrdinalIgnoreCase))
        {
            return ProbeResult.None;
        }

        if (!file.FileName.Equals("dis.dat", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.Equals("memory.dat", StringComparison.OrdinalIgnoreCase))
        {
            return ProbeResult.None;
        }

        var expectedLength = file.Directory.TitleId.Equals("PPSA02225", StringComparison.OrdinalIgnoreCase)
            ? SaveContainer.TsushimaLength
            : SaveContainer.YoteiLength;
        if (file.Length != expectedLength)
        {
            return ProbeResult.None;
        }

        var header = file.ReadHeader(sizeof(uint));
        var magic = header.Length == sizeof(uint) ? BinaryPrimitives.ReadUInt32LittleEndian(header) : uint.MaxValue;
        var matches = expectedLength == SaveContainer.TsushimaLength ? magic == SaveContainer.TsushimaMagic :
            magic is SaveContainer.YoteiMagic or 0;
        return matches
            ? ProbeResult.Match(ProbeScore.Title, $"{file.FileName} · {file.Directory.Name}")
            : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file)
        => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (Probe(file).Score < ProbeScore.Title)
        {
            throw new SaveFormatException("This is not a recognised Sucker Punch save file.");
        }

        return SuckerPunchDocument.Open(file, references);
    }
}
