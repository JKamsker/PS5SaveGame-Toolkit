using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;

namespace SaveToolkit.Plugins.UnrealGvas;

public sealed class UnrealGvasPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("engine.unreal-gvas", "Unreal Engine GVAS", "0.3.0")
    {
        Description = "Lossless Unreal SaveGame property explorer and primitive editor.",
        Games = [new GameTitle("PPSA01288", "Sackboy: A Big Adventure"), new GameTitle("PPSA01603", "Hogwarts Legacy"), new GameTitle("PPSA02199", "STAR WARS Jedi: Fallen Order")],
        Maturity = PluginMaturity.Experimental,
        References = [
            "https://github.com/trumank/uesave",
            "https://github.com/topche-katt/hlsavetool",
            "https://peteronprogramming.wordpress.com/2024/01/06/reverse-engineering-the-end-of-level-photos-in-sackboy-a-big-adventure/",
            "https://www.rfc-editor.org/rfc/rfc1950",
            "https://github.com/producedbytmsh/hl-savetools/tree/e568dedcda2611f54cc793e6d0237c749ae25094",
            "https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/tree/02525dde0fd9bc0be72982478680377dccb41246",
            "https://github.com/JKamsker/PS5SaveGame-Toolkit/tree/main/src/SaveToolkit.Codecs",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        try
        {
            if (file.ReadHeader(4).AsSpan().SequenceEqual("GVAS"u8)) return ProbeResult.Match(ProbeScore.Engine, Label(file.Directory.TitleId));
            using var stream = file.OpenRead();
            return GvasEnvelope.Probe(stream) is { } label ? ProbeResult.Match(ProbeScore.Engine, label) : ProbeResult.None;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { return ProbeResult.None; }
    }

    private static string Label(string titleId) => titleId switch
    {
        "PPSA01288" => "Sackboy save (Unreal GVAS)",
        "PPSA01603" => "Hogwarts Legacy save (Unreal GVAS)",
        "PPSA02199" => "Jedi: Fallen Order save (Unreal GVAS)",
        _ => "Unreal GVAS",
    };

    public ISaveDocument Open(ISaveFile file)
        => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (file.Length > GvasFile.MaxFileLength) throw new SaveFormatException("GVAS exceeds the 128 MiB safety limit.");
        var envelope = GvasEnvelope.Open(file.ReadAllBytes());
        return UnrealGvasSchema.Detect(envelope.File).Kind == UnrealGvasSchemaKind.HogwartsMetadata
            ? new HogwartsDocument(file.RelativePath, envelope, references)
            : new UnrealGvasDocument(file.RelativePath, envelope, references,
                file.Directory.TitleId == "PPSA01603" || envelope.File.Header.SaveClass == HogwartsImage.SaveClass
                    ? HogwartsDocument.ReadOnlyReason : null);
    }
}
