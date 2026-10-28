using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal static class RagnarokIntegrityView
{
    public static TableNode Build(ReadOnlySpan<byte> bytes, RagnarokRecordIndex index)
    {
        var probes = RagnarokAuthentication.Probe(bytes, index);
        return new([
            new("algorithm", "Candidate"), new("coverage", "Coverage"), new("range", "Byte range"),
            new("digest", "Computed digest"), new("matches", "Raw matches (unverified)"),
        ], probes.Select(probe => new TableRow($"{probe.Algorithm}/{probe.Coverage}", new Dictionary<string, ViewNode>
        {
            ["algorithm"] = new TextNode(probe.Algorithm),
            ["coverage"] = new TextNode(probe.Coverage),
            ["range"] = new TextNode($"0x{probe.Offset:X}+{probe.Length}"),
            ["digest"] = new TextNode(probe.Digest) { Monospace = true },
            ["matches"] = new TextNode(probe.MatchingOffsets.Count == 0 ? "None" : string.Join(", ", probe.MatchingOffsets.Select(offset => $"0x{offset:X}"))),
        })).ToArray())
        {
            Title = "Finite unkeyed digest hypotheses",
            Description = "Full MD5/SHA-1/SHA-256/SHA-512 digests and CRC32C in both byte orders, searched at every byte alignment. " +
                "A match is only a lead. No MAC key, signature scheme, custom seed, embedded-field exclusion or compressed coverage is established. " +
                "Negative results cannot authenticate a save or enable writes.",
        };
    }
}
