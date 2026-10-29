using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.FinalFantasy7;

public sealed class FinalFantasy7Plugin : IGamePlugin, ISaveDecoderProvider
{
    public IReadOnlyList<ISaveDecoderHook> Decoders { get; } = [new FinalFantasy7EvidenceDecoder()];

    public PluginInfo Info { get; } = new("ff7r", "Final Fantasy VII Remake / Rebirth", "0.5.0")
    {
        Description = "Experimental linked-record editor with attributed catalogs, queryable inventories/progress, regional maps, read-only folio/socket graphs, bounded stored time and previewed recovery/level/restock/AP/star actions.",
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
            "https://github.com/RyudoSynbios/game-tools-collection/tree/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils",
            "https://github.com/RyudoSynbios/game-tools-collection/tree/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor",
            "https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/LICENSE.md",
            "https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/utils/resource.ts",
            "https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils/resource.ts",
            "https://github.com/sterben-Dev10/SaveWizard-Resources/blob/main/SaveWizard-Codes/FFVII-Remake%20Codes%20%5BSaveWizard%5D.txt",
            "https://game-tools-collection.com/final-fantasy-vii-remake/save-editor",
            "https://game-tools-collection.com/final-fantasy-vii-rebirth/save-editor",
            "https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/template.ts",
            "https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-rebirth/saveEditor/utils.ts",
            RegionalMaps.ExternalMapUrl,
            "https://github.com/RyudoSynbios/game-tools-collection/blob/c7bdd6d8228304b87dcdf15e57155d092eb06fd2/src/lib/templates/final-fantasy-vii-remake/saveEditor/template.ts",
            "https://finalfantasy.fandom.com/wiki/Category:Final_Fantasy_VII_Remake_item_images",
            "https://finalfantasy.fandom.com/wiki/Final_Fantasy_Wiki:Copyrights",
            "https://www.square-enix-games.com/en_US/news/final-fantasy-vii-remake-screenshots",
            "https://blog.playstation.com/archive/2019/11/25/new-final-fantasy-vii-remake-screens-feature-moogles-weapon-abilities-materia-mini-games-and-more",
            "https://www.gamerguides.com/final-fantasy-vii-remake-intergrade/guide/appendix/items/consumables",
            "https://www.powerpyx.com/terms-of-service/",
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

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        var schema = Ff7Schema.ForTitle(file.Directory.TitleId);
        if (schema is null || !schema.TryGetRole(file.FileName, out var common) ||
            file.Length != schema.FileLength(common))
        {
            throw new SaveFormatException("Unsupported FFVII title, file name or schema size.");
        }

        var bytes = file.ReadAllBytes();
        var records = LinkedRecords.Parse(bytes, schema, common);
        return new FinalFantasy7Document(file.RelativePath, file.FileName, bytes, file.Directory.TitleId, schema, common, records, references);
    }
}
