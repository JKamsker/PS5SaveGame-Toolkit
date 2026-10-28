using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

public sealed class GodOfWarRagnarokPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("gow-ragnarok", "God of War Ragnarök", "0.1.0")
    {
        Description = "Read-only PS5 header, wallet, record index and preferences explorer. Integrity is unresolved.",
        Games = [.. new[] { "PPSA08329", "PPSA08330", "PPSA08331", "PPSA08332", "PPSA08333", "PPSA08334", "PPSA25043", "PPSA25044" }
            .Select(id => new GameTitle(id, "God of War Ragnarök"))],
        Maturity = PluginMaturity.ReadOnly,
        References =
        [
            "https://github.com/sterben-Dev10/SaveWizard-Resources",
            "https://github.com/Alexsander720/continue/blob/main/plugin/py_modules/ddsaves/god_of_war_ragnarok.py",
            "https://bucanero.github.io/apollo-lib/savewizard.html",
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
    {
        if (!Probe(file).IsMatch || file.Length > RagnarokFormat.MaximumFileSize)
        {
            throw new SaveFormatException("Unsupported Ragnarök PS5 file name, title, size or header revision.");
        }

        var bytes = file.ReadAllBytes();
        var preferences = file.FileName.Equals("userpreferences", StringComparison.OrdinalIgnoreCase);
        var index = RagnarokRecordIndex.Parse(bytes, preferences);
        return new RagnarokDocument(file.RelativePath, file.FileName, bytes, index);
    }
}
