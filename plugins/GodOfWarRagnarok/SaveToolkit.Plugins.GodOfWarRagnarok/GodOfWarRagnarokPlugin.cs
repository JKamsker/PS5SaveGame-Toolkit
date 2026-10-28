using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

public sealed class GodOfWarRagnarokPlugin : IGamePlugin, ISaveDecoderProvider
{
    public IReadOnlyList<ISaveDecoderHook> Decoders => [new RagnarokEvidenceDecoder()];
    public PluginInfo Info { get; } = new("gow-ragnarok", "God of War Ragnarök", "0.5.0")
    {
        Description = "Experimental PS5 existing wallet counter/play-time edits and planned restore, with paged research views. " + RagnarokWrites.IntegrityWarning,
        Games = [.. new[] { "PPSA08329", "PPSA08330", "PPSA08331", "PPSA08332", "PPSA08333", "PPSA08334", "PPSA25043", "PPSA25044" }
            .Select(id => new GameTitle(id, "God of War Ragnarök"))],
        Maturity = PluginMaturity.Experimental,
        References =
        [
            "https://github.com/sterben-Dev10/SaveWizard-Resources",
            "https://github.com/Alexsander720/continue/blob/main/plugin/py_modules/ddsaves/god_of_war_ragnarok.py",
            "https://bucanero.github.io/apollo-lib/savewizard.html",
            "https://godofwar.fandom.com/wiki/Resources/GOW_%28Ragnar%C3%B6k%29",
            "https://www.fandom.com/licensing",
            "https://github.com/bucanero/apollo-patches/blob/273f032b4c31c017fed5c1588f209b8faf87ba6c/PS4/CUSA34384.savepatch",
            "https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py",
            "https://www.gnu.org/licenses/gpl-3.0.html",
            "https://godofwar.fandom.com/wiki/Leviathan_Axe",
            "https://godofwar.fandom.com/wiki/Blades_of_Chaos",
            "https://godofwar.fandom.com/wiki/Draupnir_Spear",
            "https://godofwar.fandom.com/wiki/Skill_Labors_%28Ragnar%C3%B6k%29",
            "https://creativecommons.org/licenses/by-sa/3.0/",
            "https://mapgenie.io/god-of-war-ragnarok",
            "https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/item%23.zip",
            "https://github.com/mooofin/God-of-War-Ragnarok/blob/7b1730217b92d27ab910d6d421efbd503cc109b2/gow.CT",
            "https://github.com/xcier/GoW-2018-PS4-editor/blob/9342d5f3967ef74bac1161581dc4eab8bb41760d/app/resources/database/gow2018_items.json",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (!Info.Games.Any(g => string.Equals(g.TitleId, file.Directory.TitleId, StringComparison.OrdinalIgnoreCase)) ||
            !RagnarokFormat.TryFileKind(file.FileName, out var preferences))
        {
            return ProbeResult.None;
        }

        return RagnarokFormat.MatchesHeader(file.ReadHeader(RagnarokFormat.HeaderSize), file.Length, preferences)
            ? ProbeResult.Match(ProbeScore.Title, preferences ? "Ragnarök user preferences" : $"Ragnarök gameplay/history · {file.FileName}")
            : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file)
        => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (!Probe(file).IsMatch || file.Length > RagnarokFormat.MaximumFileSize)
        {
            throw new SaveFormatException("Unsupported Ragnarök PS5 file name, title, size or header revision.");
        }

        var bytes = file.ReadAllBytes();
        var preferences = file.FileName.Equals("userpreferences", StringComparison.OrdinalIgnoreCase);
        var index = RagnarokRecordIndex.Parse(bytes, preferences);
        return new RagnarokDocument(file.RelativePath, file.FileName, bytes, index, new RagnarokReferences(references));
    }
}
