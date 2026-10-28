using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Plugins.Pragmata.Crypto;

namespace SaveToolkit.Plugins.Pragmata;

public sealed class PragmataPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("pragmata", "Pragmata", "0.1.0")
    {
        Description = "DSSS/Mandarin editor with inventory, synchronized clocks and a lazy typed explorer.",
        Games = [new GameTitle("PPSA02530", "Pragmata")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/kvasszn/ree-save-editor", "https://github.com/google/cityhash"],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (!string.Equals(file.Directory.TitleId, "PPSA02530", StringComparison.OrdinalIgnoreCase) ||
            !file.FileName.StartsWith("data", StringComparison.OrdinalIgnoreCase) ||
            !file.FileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
            !DsssContainer.HasSupportedHeader(file.ReadHeader(DsssContainer.HeaderSize)))
            return ProbeResult.None;
        return ProbeResult.Match(ProbeScore.Title, file.FileName == "data00-1.bin" ? "Pragmata system data" : "Pragmata gameplay save");
    }

    public ISaveDocument Open(ISaveFile file) => new PragmataDocument(file.RelativePath, file.ReadAllBytes());
}
