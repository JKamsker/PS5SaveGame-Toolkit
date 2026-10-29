using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;

namespace SaveToolkit.Plugins.UnrealGvas;

public sealed class UnrealGvasPlugin : IGamePlugin, ISaveDecoderProvider
{
    public IReadOnlyList<ISaveDecoderHook> Decoders { get; } = [new HogwartsSaveDecoder()];

    public PluginInfo Info { get; } = new("engine.unreal-gvas", "Unreal Engine GVAS", "0.4.0")
    {
        Description = "Lossless Unreal SaveGame property explorer and primitive editor.",
        Games = [new GameTitle("PPSA01288", "Sackboy: A Big Adventure"), new GameTitle("PPSA01603", "Hogwarts Legacy"), new GameTitle("PPSA02199", "STAR WARS Jedi: Fallen Order")],
        Maturity = PluginMaturity.Experimental,
        References = [
            "https://github.com/trumank/uesave",
            "https://github.com/trumank/uesave/tree/1917a22ab69e9a4045e613d9d7f39a49fe00bd46",
            "https://github.com/JKamsker/PS5SaveGame-Toolkit",
            "https://github.com/Donpedro13/GingerBreadImageExtractor/tree/3b26966a04a2b6e20d61962ef6fcb84242bb7a1f",
            "https://reveng.sourceforge.io/crc-catalogue/17plus.htm",
            "https://wiki.imagisphere.me/Category:Sackboy:_A_Big_Adventure_Levels",
            "https://wiki.imagisphere.me/Zom_Zom%27s_Shop_%28Sackboy%3A_A_Big_Adventure%29",
            "https://www.powerpyx.com/star-wars-jedi-fallen-order-all-collectible-locations-guide/",
            "https://github.com/topche-katt/hlsavetool",
            "https://peteronprogramming.wordpress.com/2024/01/06/reverse-engineering-the-end-of-level-photos-in-sackboy-a-big-adventure/",
            "https://www.rfc-editor.org/rfc/rfc1950",
            "https://github.com/producedbytmsh/hl-savetools/tree/e568dedcda2611f54cc793e6d0237c749ae25094",
            "https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/tree/02525dde0fd9bc0be72982478680377dccb41246",
            "https://hogwartslegacy.gamesref.com/gear",
            "https://cdn.eip.gg/hogwarts-legacy/icons/gear/",
            "https://wiki.imagisphere.me/Category:Sackboy:_A_Big_Adventure_Costume_Icons",
            "https://github.com/JKamsker/PS5SaveGame-Toolkit/tree/main/src/SaveToolkit.Codecs",
            HogwartsInventorySlots.SourceUrl,
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        try
        {
            if (file.Length > GvasFile.MaxFileLength) return ProbeResult.None;
            using var stream = file.OpenRead();
            var label = GvasEnvelope.Probe(stream);
            return label is null ? ProbeResult.None : ProbeResult.Match(ProbeScore.Engine,
                label == "Unreal GVAS" ? Label(file.Directory.TitleId) : label);
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
        var schema = UnrealGvasSchema.Detect(envelope.File);
        if (schema.Kind == UnrealGvasSchemaKind.HogwartsMetadata)
            return new HogwartsDocument(file.RelativePath, envelope, references);
        if (file.Directory.TitleId == "PPSA01603" || envelope.File.Header.SaveClass == HogwartsImage.SaveClass)
            return new UnrealGvasDocument(file.RelativePath, envelope, references, HogwartsDocument.ReadOnlyReason);
        return new GvasQueryableDocument(file.RelativePath, envelope, schema, references);
    }
}
