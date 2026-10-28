using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>F1 hook exposing only selected gameplay scalars and serials; no character/account IDs.</summary>
internal sealed class GearboxEvidenceDecoder : ISaveDecoder
{
    public string Id => "gearbox-evidence";
    public bool CanDecode(ISaveFile file) => new GearboxPlugin().Probe(file).IsMatch;

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes) throw new InvalidDataException("Evidence file exceeds decode limit.");
        var evidence = IndependentSaveProbe.Read(file.ReadAllBytes(), file.Directory.TitleId == "PPSA01493", file.FileName == "profile");
        var builder = new SemanticTreeBuilder(Id, limits);
        if (limits.MaxDepth < 2)
        {
            builder.MarkTruncated("Evidence paths require two levels.");
            return builder.Build();
        }
        if (file.FileName != "profile")
        {
            if (!builder.Add("character/xp", "integer", evidence.Experience.ToString(CultureInfo.InvariantCulture))) return builder.Build();
            if (!builder.Add("currency/618814354", "integer", evidence.Money.ToString(CultureInfo.InvariantCulture))) return builder.Build();
        }
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var serial in evidence.Serials)
        {
            // Content identity aligns unchanged serials across reorder; duplicates retain multiplicity.
            // Replaced serials appear removed/added: no durable item identity is claimed across captures.
            var hash = Convert.ToHexStringLower(SHA256.HashData(serial));
            var occurrence = occurrences.GetValueOrDefault(hash);
            occurrences[hash] = occurrence + 1;
            if (!builder.Add($"serials[{hash}:{occurrence}]/code", "bytes", Convert.ToBase64String(serial))) break;
        }
        return builder.Build();
    }
}
