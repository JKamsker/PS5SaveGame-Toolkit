using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.TalesOfArise;

public sealed class TalesOfArisePlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("tales-of-arise", "Tales of Arise", "0.1.0")
    {
        Description = "PS5 gameplay decryption, integrity verification and experimental fixed-size editing.",
        Games = [new GameTitle("PPSA02745", "Tales of Arise") { Region = "EU" }],
        Maturity = PluginMaturity.Experimental,
        References =
        [
            "https://github.com/ItsAllAboutTheCode/save-converters/tree/main/save_convert/tales_of/arise",
            "https://web.save-editor.com/bbs/patchcode/ps4/bbs.cgi?list=pickup&num=30070",
            "https://github.com/Sora3100/Tales_of_Cheat_Tables/blob/master/Arise.CT",
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

    public ISaveDocument Open(ISaveFile file) => new AriseDocument(file.RelativePath, file.FileName, AriseFormat.Decode(file.ReadAllBytes()));

    private static bool IsGameplayName(string name) => name.Length == "SaveData_000.sav".Length
        && name.StartsWith("SaveData_", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)
        && name.AsSpan(9, 3).IndexOfAnyExceptInRange('0', '9') < 0;
}
