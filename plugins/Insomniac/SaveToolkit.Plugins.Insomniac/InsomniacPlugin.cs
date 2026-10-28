using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Insomniac;

public sealed class InsomniacPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("insomniac", "Ratchet & Clank: Rift Apart", "0.1.0")
    {
        Description = "Lossless DAT1 container and section explorer for Rift Apart saves.",
        Games = [new GameTitle("PPSA01474", "Ratchet & Clank: Rift Apart")],
        Maturity = PluginMaturity.ReadOnly,
        References =
        [
            "https://github.com/Tkachov/ALERT/blob/main/dat1lib/types/dat1.py",
            "https://github.com/Tkachov/ALERT/wiki/Formats",
            "https://github.com/VELD-Dev/riftripper",
            "https://ninjasaves.com/cheats/ratchet-clank-rift-apart-ppsa01474",
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

    public ISaveDocument Open(ISaveFile file)
    {
        try
        {
            return new InsomniacDocument(file.RelativePath, Dat1Container.Parse(file.ReadAllBytes()), file.FileName);
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
