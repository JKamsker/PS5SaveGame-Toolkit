using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.SuckerPunch;

public sealed class SuckerPunchPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("suckerpunch", "Ghost of Tsushima / Ghost of Yōtei", "0.1.0")
    {
        Description = "Read-only parser for the fixed-size Sucker Punch save containers.",
        Games =
        [
            new GameTitle("PPSA02225", "Ghost of Tsushima"),
            new GameTitle("PPSA26344", "Ghost of Yōtei"),
        ],
        Maturity = PluginMaturity.ReadOnly,
        References =
        [
            "https://github.com/KOVRlN/ghostoftsushima-save-converter/blob/main/TECHNICAL.md",
            "https://github.com/KOVRlN/ghostoftsushima-save-converter",
            "https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/Ghost%20of%20Tsushima%20Codes%20%5BSaveWizard%5D.txt",
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
        return file.Length == expectedLength
            ? ProbeResult.Match(ProbeScore.Title, $"{file.FileName} · {file.Directory.Name}")
            : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file)
    {
        if (Probe(file).Score < ProbeScore.Title)
        {
            throw new SaveFormatException("This is not a recognised Sucker Punch save file.");
        }

        return SuckerPunchDocument.Open(file);
    }
}
