using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.FinalFantasy7;

public sealed class FinalFantasy7Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("ff7r", "Final Fantasy VII Remake / Rebirth", "0.2.0")
    {
        Description = "Experimental linked-record explorer with in-place edits for researched gil, inventory quantities and materia AP.",
        Games =
        [
            new GameTitle("PPSA01911", "Final Fantasy VII Remake Intergrade"),
            new GameTitle("PPSA08664", "Final Fantasy VII Rebirth"),
            new GameTitle("PPSA18936", "Final Fantasy VII Rebirth Demo"),
        ],
        Maturity = PluginMaturity.Experimental,
        References =
        [
            "https://github.com/Xeeynamo/KingdomSaveEditor/tree/master/KHSave.LibFf7Remake",
            "https://github.com/RyudoSynbios/game-tools-collection/tree/master/src/lib/templates/final-fantasy-vii-rebirth",
            "https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/FFVII-Remake%20Codes%20%5BSaveWizard%5D.txt",
            "https://game-tools-collection.com/final-fantasy-vii-remake/save-editor",
            "https://game-tools-collection.com/final-fantasy-vii-rebirth/save-editor",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        var schema = Ff7Schema.ForTitle(file.Directory.TitleId);
        if (schema is null || !schema.TryGetRole(file.FileName, out var common) ||
            file.Length != schema.FileLength(common))
        {
            return ProbeResult.None;
        }

        return schema.MatchesFirstHeader(file.ReadHeader(LinkedRecords.ContentOffset), common)
            ? ProbeResult.Match(ProbeScore.Title, common ? "Common/system state" : $"Gameplay · {file.FileName}")
            : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file)
    {
        var schema = Ff7Schema.ForTitle(file.Directory.TitleId);
        if (schema is null || !schema.TryGetRole(file.FileName, out var common) ||
            file.Length != schema.FileLength(common))
        {
            throw new SaveFormatException("Unsupported FFVII title, file name or schema size.");
        }

        var bytes = file.ReadAllBytes();
        var records = LinkedRecords.Parse(bytes, schema, common);
        return new FinalFantasy7Document(file.RelativePath, file.FileName, bytes, schema, common, records);
    }
}
