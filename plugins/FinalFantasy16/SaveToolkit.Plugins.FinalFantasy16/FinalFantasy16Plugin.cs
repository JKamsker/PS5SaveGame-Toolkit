using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy16;

public sealed class FinalFantasy16Plugin : IGamePlugin, ISaveDecoderProvider
{
    public IReadOnlyList<ISaveDecoderHook> Decoders { get; } = [new FaithEvidenceDecoder()];

    public PluginInfo Info { get; } = new("ffxvi", "Final Fantasy XVI", "0.7.0")
    {
        Description = "Paged PS5 inventories, filtered quantity previews, Eikon ability reference trees and a location guide; experimental fixed-width writes. Not console-validated.",
        Games = [new GameTitle("PPSA10665", "Final Fantasy XVI"), new GameTitle("PPSA13803", "Final Fantasy XVI Demo")],
        Maturity = PluginMaturity.Experimental,
        References = [
            FaithMapArt.OfficialUrl,
            FaithMapArt.RegionalUrl,
            "https://game8.co/games/Final-Fantasy-XVI/archives/415576",
            "https://game8.co/games/Final-Fantasy-XVI/archives/414895",
            "https://game8.co/games/Final-Fantasy-XVI/archives/415577",
            "https://game8.co/games/Final-Fantasy-XVI/archives/409998",
            "https://game8.co/games/Final-Fantasy-XVI/archives/416024",
            "https://game8.co/games/Final-Fantasy-XVI/archives/409930",
            "https://github.com/Nenkai/ffxvi-modding/tree/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids",
            "https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/item_ids.csv",
            "https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/equip_item_ids.csv",
            "https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/gamemap_ids.csv",
            "https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/bnpc_ids.csv",
            "https://github.com/Nenkai/ffxvi-modding/blob/4b5e7eb349d2decd776b60b0f2d0f2af7aaaac03/docs/resources/ids/enpc_ids.csv",
            "https://finalfantasy.fandom.com/wiki/Template:Navbox_abilities_FFXVI?oldid=3770527",
            "https://finalfantasy.fandom.com/wiki/Leviathan_(Final_Fantasy_XVI)?oldid=4005603",
            "https://finalfantasy.fandom.com/wiki/Ultima_(Final_Fantasy_XVI)?oldid=4045298",
            FaithLocationView.GuideUrl,
            "https://nenkai.github.io/ffxvi-modding/resources/other/save_files/",
            "https://github.com/Nenkai/010GameTemplates/blob/f31430e32f099d66cc22d609a7926988e041c409/Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt",
            "https://github.com/Nenkai/010GameTemplates/blob/4a3685f9b63f72ca49e24ac2f18420aaad2762b8/Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt",
            "https://nenkai.github.io/ffxvi-modding/resources/ids/item_ids/",
            "https://github.com/Nenkai/FF16Tools/blob/b87c2f22a8ea630cb03f7b2198af84c7709a1b0c/FF16Tools.Files/Save/FaithSaveFile.cs",
            "https://finalfantasy.fandom.com/wiki/Final_Fantasy_XVI_translations?oldid=4007473",
            "https://www.fandom.com/licensing",
            "https://community.fandom.com/wiki/Help:Licensing?oldid=3983537",
            "https://finalfantasy.fandom.com/wiki/Gil?oldid=4063996",
            "https://finalfantasy.fandom.com/wiki/Renown?oldid=3741166",
            "https://finalfantasy.fandom.com/wiki/Ability_Points?oldid=4035790",
            "https://github.com/Nenkai/ffxvi-nex-layouts/tree/d3ad0627a29af168916ad574dadbca6759e2ac48",
            "https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/GameMap.layout",
            "https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/SummonMode.layout",
            "https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/Skill.layout",
            "https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/UseItem.layout",
            "https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/Item.layout",
            "https://github.com/Nenkai/ffxvi-nex-layouts/blob/d3ad0627a29af168916ad574dadbca6759e2ac48/ParamGrow.layout",
            "https://finalfantasy.fandom.com/wiki/Fafnir?oldid=3987229",
            "https://finalfantasy.fandom.com/wiki/Wall_of_Memories?oldid=3968454",
            "https://finalfantasy.fandom.com/wiki/Journal?oldid=3776380",
            "https://finalfantasy.fandom.com/wiki/Patron%27s_Whisper?oldid=3965500",
            "https://finalfantasy.fandom.com/wiki/Joshua_Rosfield?oldid=4047110",
            "https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/LogosUnleashed/Mod.cs",
            "https://github.com/mThund3R/Logos-Unleashed/blob/6424cb4e60bd31005ef7f0cfb42d4e5360660fc6/LICENSE",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (!FaithSaveIdentity.IsSupportedFile(file))
        {
            return ProbeResult.None;
        }

        if (!FaithSaveIdentity.HasMatchingHeader(file))
        {
            return ProbeResult.None;
        }

        var system = file.FileName == "system-save-data";
        return ProbeResult.Match(ProbeScore.Title, system ? "System/settings" :
            file.FileName == "game-save-data" ? "Manual/special gameplay save" : $"Autosave {file.FileName[^2..]}");
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (!FaithSaveIdentity.IsSupportedFile(file))
        {
            throw new SaveFormatException("Expected an FFXVI retail or demo PS5 save file.");
        }

        if (file.Length > FaithContainer.MaximumFileSize)
        {
            throw new SaveFormatException("Faith save exceeds the supported 64 MiB limit.");
        }

        var bytes = file.ReadAllBytes();
        var container = FaithContainer.Parse(bytes);
        if ((file.FileName == "system-save-data") != container.IsSystem)
        {
            throw new SaveFormatException("Faith header does not match the file's gameplay/system role.");
        }

        return new FaithDocument(file, bytes, container, FaithSchema.ForTitle(file.Directory.TitleId), references);
    }

}
