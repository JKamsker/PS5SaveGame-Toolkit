using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Plugins.UnrealGvas.Gvas;

namespace SaveToolkit.Plugins.UnrealGvas;

public sealed class UnrealGvasPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("engine.unreal-gvas", "Unreal Engine GVAS", "0.1.0")
    {
        Description = "Lossless Unreal SaveGame property explorer and primitive editor.",
        Games = [new GameTitle("PPSA01603", "Hogwarts Legacy"), new GameTitle("PPSA02199", "STAR WARS Jedi: Fallen Order")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/trumank/uesave", "https://github.com/topche-katt/hlsavetool"],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        try
        {
            if (file.ReadHeader(4).AsSpan().SequenceEqual("GVAS"u8)) return ProbeResult.Match(ProbeScore.Engine, "Unreal GVAS");
            using var stream = file.OpenRead();
            return GvasEnvelope.Probe(stream) is { } label ? ProbeResult.Match(ProbeScore.Engine, label) : ProbeResult.None;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { return ProbeResult.None; }
    }

    public ISaveDocument Open(ISaveFile file)
    {
        if (file.Length > GvasFile.MaxFileLength) throw new SaveFormatException("GVAS exceeds the 128 MiB safety limit.");
        return new UnrealGvasDocument(file.RelativePath, GvasEnvelope.Open(file.ReadAllBytes()));
    }
}
