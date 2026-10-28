using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Plugins.Pragmata.Crypto;

namespace SaveToolkit.Plugins.Pragmata;

public sealed class PragmataPlugin : IGamePlugin
{
    private const string SlotDirectoryPrefix = "SAVESERVICE-LINE-0-";
    private const string SlotDirectorySuffix = "Slot";
    private const string GameplaySaveLabel = "Gameplay save";

    public PluginInfo Info { get; } = new("pragmata", "Pragmata", "0.1.0")
    {
        Description = "DSSS/Mandarin editor with inventory, synchronized clocks and a lazy typed explorer.",
        Games = [new GameTitle("PPSA02530", "Pragmata")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/kvasszn/ree-save-editor", "https://github.com/google/cityhash"],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (!string.Equals(file.Directory.TitleId, "PPSA02530", StringComparison.OrdinalIgnoreCase) ||
            !file.FileName.StartsWith("data", StringComparison.OrdinalIgnoreCase) ||
            !file.FileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
            !DsssContainer.HasSupportedHeader(file.ReadHeader(DsssContainer.HeaderSize)))
            return ProbeResult.None;
        return ProbeResult.Match(ProbeScore.Title,
            string.Equals(file.FileName, "data00-1.bin", StringComparison.OrdinalIgnoreCase) ? "System data" : GameplayLabel(file.Directory.Name));
    }

    public ISaveDocument Open(ISaveFile file) => new PragmataDocument(file.RelativePath, file.ReadAllBytes());

    private static string GameplayLabel(string directoryName)
    {
        if (!directoryName.StartsWith(SlotDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
            return GameplaySaveLabel;

        var slot = directoryName.AsSpan(SlotDirectoryPrefix.Length);
        if (slot.EndsWith(SlotDirectorySuffix, StringComparison.OrdinalIgnoreCase))
            slot = slot[..^SlotDirectorySuffix.Length];
        else if (!slot.SequenceEqual("0"))
            return GameplaySaveLabel;

        return int.TryParse(slot, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? FormattableString.Invariant($"{GameplaySaveLabel}, slot {index}") : GameplaySaveLabel;
    }
}
