using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using System.Buffers.Binary;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy16;

public sealed class FinalFantasy16Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("ffxvi", "Final Fantasy XVI", "0.4.0")
    {
        Description = "Named PS5 inventories and experimental fixed-width inventory, equipment, ability and player-context writes with F6 quantity previews. Not console-validated.",
        Games = [new GameTitle("PPSA10665", "Final Fantasy XVI"), new GameTitle("PPSA13803", "Final Fantasy XVI Demo")],
        Maturity = PluginMaturity.Experimental,
        References = [
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
        if (!IsSupportedTitle(file.Directory.TitleId) || !IsSaveName(file.FileName))
        {
            return ProbeResult.None;
        }

        var header = file.ReadHeader(FaithContainer.HeaderSize);
        if (header.Length < FaithContainer.HeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(header) != FaithContainer.HeaderSize)
        {
            return ProbeResult.None;
        }

        var version = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(FaithContainer.VersionOffset));
        var system = file.FileName == "system-save-data";
        return version == (system ? FaithContainer.SystemVersion : FaithContainer.GameVersion)
            ? ProbeResult.Match(ProbeScore.Title, system ? "System/settings" :
                file.FileName == "game-save-data" ? "Manual/special gameplay save" : $"Autosave {file.FileName[^2..]}")
            : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (!IsSupportedTitle(file.Directory.TitleId) || !IsSaveName(file.FileName))
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

    private static bool IsSupportedTitle(string title) => title is "PPSA10665" or "PPSA13803";

    private static bool IsSaveName(string name) => name is "game-save-data" or "system-save-data" ||
        (name.Length == "game-save-data00".Length && name.StartsWith("game-save-data0", StringComparison.Ordinal) &&
         name[^1] is >= '0' and <= '9');
}
