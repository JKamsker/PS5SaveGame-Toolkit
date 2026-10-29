using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.TalesOfArise;

public sealed class TalesOfArisePlugin : IGamePlugin, ISaveDecoderProvider
{
    public IReadOnlyList<ISaveDecoderHook> Decoders { get; } = Array.AsReadOnly<ISaveDecoderHook>([new AriseDiff()]);

    public PluginInfo Info { get; } = new("tales-of-arise", "Tales of Arise", "0.2.0")
    {
        Description = "PS5 gameplay decryption, integrity verification and experimental fixed-size editing.",
        Games = [new GameTitle("PPSA02745", "Tales of Arise") { Region = "EU" }],
        Maturity = PluginMaturity.Experimental,
        References =
        [
            "https://github.com/ItsAllAboutTheCode/save-converters/tree/main/save_convert/tales_of/arise",
            AriseSchema.SectionSource,
            AriseSchema.CpSource,
            "https://fearlessrevolution.com/viewtopic.php?t=17367#p210160",
            "https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30070",
            "https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30162",
            "https://web.save-editor.com/bbs/patchcode/ps4/dl.cgi?file=31135.txt",
            "https://raw.githubusercontent.com/Sora3100/Tales_of_Cheat_Tables/c92871084d714d98b3854ba9fbb4e3f022145d7e/Arise.CT",
            "https://github.com/JKamsker/PS5SaveGame-Toolkit",
            "https://aselia.fandom.com/wiki/Tales_of_Arise",
            "https://aselia.fandom.com/wiki/ToA_-_Owls",
            "https://aselia.fandom.com/wiki/ToA_-_Camps",
            AriseAssets.MapSource,
            AriseAssets.ArteSource,
            "https://www.gamerguides.com/tales-of-arise/guide/characters/rinwell/artes",
            .. AriseAssets.DescriptionSources,
            .. AriseSchema.CatalogSources,
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (file.Directory.TitleId != "PPSA02745" || !IsGameplayName(file.FileName) || file.Length != AriseFormat.FileSize)
        {
            return ProbeResult.None;
        }

        return AriseFormat.HasHeader(file.ReadHeader(AriseFormat.CipherOffset))
            ? ProbeResult.Match(ProbeScore.Title, $"Gameplay · {file.FileName}")
            : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references) =>
        new AriseDocument(file.RelativePath, file.FileName, AriseFormat.Decode(file.ReadAllBytes()), references);

    private static bool IsGameplayName(string name) => name.Length == "SaveData_000.sav".Length
        && name.StartsWith("SaveData_", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)
        && name.AsSpan(9, 3).IndexOfAnyExceptInRange('0', '9') < 0;
}
