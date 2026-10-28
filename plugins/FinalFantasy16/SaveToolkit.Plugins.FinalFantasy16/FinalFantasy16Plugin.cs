using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using System.Buffers.Binary;

namespace SaveToolkit.Plugins.FinalFantasy16;

public sealed class FinalFantasy16Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("ffxvi", "Final Fantasy XVI", "0.1.0")
    {
        Description = "PS5 Faith containers, existing inventory stacks, and a lazy structural explorer.",
        Games = [new GameTitle("PPSA10665", "Final Fantasy XVI"), new GameTitle("PPSA13803", "Final Fantasy XVI Demo")],
        Maturity = PluginMaturity.Experimental,
        References = [
            "https://nenkai.github.io/ffxvi-modding/resources/other/save_files/",
            "https://github.com/Nenkai/010GameTemplates/blob/f31430e32f099d66cc22d609a7926988e041c409/Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt",
            "https://nenkai.github.io/ffxvi-modding/resources/ids/item_ids/",
            "https://github.com/Nenkai/FF16Tools/blob/master/FF16Tools.Files/Save/FaithSaveFile.cs",
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

    public ISaveDocument Open(ISaveFile file)
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

        return new FaithDocument(file, bytes, container, FaithSchema.ForTitle(file.Directory.TitleId));
    }

    private static bool IsSupportedTitle(string title) => title is "PPSA10665" or "PPSA13803";

    private static bool IsSaveName(string name) => name is "game-save-data" or "system-save-data" ||
        (name.Length == "game-save-data00".Length && name.StartsWith("game-save-data0", StringComparison.Ordinal) &&
         name[^1] is >= '0' and <= '9');
}
