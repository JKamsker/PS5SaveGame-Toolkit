using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.AstroPlayroom;

public sealed class AstroPlayroomPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("astro-playroom", "ASTRO's PLAYROOM", "0.1.0")
    {
        Description = "Preserving editor for the decrypted 1 MiB PreInstall save allocation.",
        Games = [new GameTitle("PPSA01325", "ASTRO's PLAYROOM")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://www.reddit.com/r/PS5_Jailbreak/comments/1wemi58/astros_playroom_special_bots_dont_show_up/"],
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

    public ISaveDocument Open(ISaveFile file)
    {
        if (file.Length != AstroAllocation.Size)
        {
            throw new SaveFormatException("ASTRO's PLAYROOM memory.dat must be exactly 1 MiB.");
        }

        return new AstroDocument(file.RelativePath, AstroAllocation.Parse(file.ReadAllBytes()));
    }
}
