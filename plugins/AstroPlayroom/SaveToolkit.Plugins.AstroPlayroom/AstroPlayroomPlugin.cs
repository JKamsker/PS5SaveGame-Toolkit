using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.AstroPlayroom;

public sealed class AstroPlayroomPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("astro-playroom", "ASTRO's PLAYROOM", "0.2.0")
    {
        Description = "Preserving editor for the decrypted 1 MiB PreInstall save allocation.",
        Games = [new GameTitle("PPSA01325", "ASTRO's PLAYROOM")],
        Maturity = PluginMaturity.Experimental,
        References = [
            "https://www.reddit.com/r/PS5_Jailbreak/comments/1wemi58/astros_playroom_special_bots_dont_show_up/",
            "https://astrosplayroom.fandom.com/wiki/Artifacts",
            "https://astro-bot-series.fandom.com/wiki/Artefacts",
            "https://astrosplayroom.fandom.com/wiki/Puzzle_Pieces",
            "https://astrosplayroom.fandom.com/wiki/PlayStation_Labo",
            "https://astrosplayroom.fandom.com/wiki/CPU_Plaza",
            "https://community.fandom.com/wiki/Help:Licensing",
            "https://www.powerpyx.com/astros-playroom-all-collectible-locations-artefacts-puzzle-pieces/",
            "https://creativecommons.org/licenses/by-sa/3.0/",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (file.Directory.TitleId != "PPSA01325" || file.Length != AstroAllocation.Size ||
            !string.Equals(file.PathInDirectory, "memory.dat", StringComparison.OrdinalIgnoreCase))
        {
            return ProbeResult.None;
        }

        var header = file.ReadHeader(AstroAllocation.TextOffset + AstroAllocation.Signature.Length);
        return AstroAllocation.HasSignature(header)
            ? ProbeResult.Match(ProbeScore.Title, "ASTRO's PLAYROOM progress")
            : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (file.Length != AstroAllocation.Size)
        {
            throw new SaveFormatException("ASTRO's PLAYROOM memory.dat must be exactly 1 MiB.");
        }

        return new AstroDocument(file.RelativePath, AstroAllocation.Parse(file.ReadAllBytes()), references);
    }
}
