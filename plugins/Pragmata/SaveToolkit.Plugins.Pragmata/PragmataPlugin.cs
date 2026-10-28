using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;
using SaveToolkit.Plugins.Pragmata.Crypto;

namespace SaveToolkit.Plugins.Pragmata;

public sealed class PragmataPlugin : IGamePlugin
{
    private const string SlotDirectoryPrefix = "SAVESERVICE-LINE-0-";
    private const string SlotDirectorySuffix = "Slot";
    private const string GameplaySaveLabel = "Gameplay save";

    public PluginInfo Info { get; } = new("pragmata", "Pragmata", "0.5.0")
    {
        Description = "Experimental DSSS/Mandarin fixed-size editor with attributed item-ID replacement, queryable gameplay edits, planned quantities and synchronized clocks. Console acceptance and RSA trailer integrity are unverified.",
        Games = [new GameTitle("PPSA02530", "Pragmata")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/kvasszn/ree-save-editor", "https://github.com/google/cityhash",
            Format.HashNames.Source, "https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/21",
            "https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31",
            "https://github.com/vaultic13/neuro-pragmata/tree/ae2ee572410e353ceae19bdc23f83f71687267dd",
            "https://github.com/dzxrly/PRAGMATA-MaximumTimeLimit/tree/cfa68e987bf126daa3ce62d589ac2b7ef05abf2b",
            "https://pragmata.fandom.com/wiki/Category%3AItems", Views.CheckpointMap.ExternalMapUrl],
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

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references) =>
        new PragmataDocument(file.RelativePath, file.ReadAllBytes(), references);

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
