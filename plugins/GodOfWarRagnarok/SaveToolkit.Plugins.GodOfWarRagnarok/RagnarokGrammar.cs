using System.Buffers.Binary;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal enum RagnarokSpanKind
{
    Header,
    Metadata,
    DirectoryHeader,
    DirectoryIds,
    PairedWords,
    SecondaryHeader,
    SecondaryIds,
    WalletHeader,
    WalletRows,
    TrailingSentinel,
    WalletBlock,
    WalletIds,
    WalletIndices,
    NestedLinks,
    FlagGroups,
    Opaque,
}

internal sealed record RagnarokSpan(RagnarokSpanKind Kind, int Offset, int Length, int Count, string Confidence);

internal sealed record RagnarokLengthCandidate(int Offset, uint Length, string Basis);

internal sealed record RagnarokHashCandidate(int Offset, ulong Value, string Role);

internal sealed record RagnarokCompressionCandidate(int Offset, string Signature, string Note);

/// <summary>Bounded, read-only evidence produced by the clean-room grammar harness.</summary>
internal sealed record RagnarokGrammarEvidence(
    IReadOnlyList<RagnarokSpan> Spans,
    IReadOnlyList<RagnarokLengthCandidate> LengthCandidates,
    IReadOnlyList<RagnarokHashCandidate> HashCandidates,
    IReadOnlyList<RagnarokCompressionCandidate> CompressionCandidates)
{
    public int KnownBytes => Spans.Where(s => s.Kind != RagnarokSpanKind.Opaque).Sum(s => s.Length);
    public int OpaqueBytes => Spans.Where(s => s.Kind == RagnarokSpanKind.Opaque).Sum(s => s.Length);
}

internal static class RagnarokGrammar
{
    private const int MaximumCandidates = 4096;

    public static RagnarokGrammarEvidence Analyze(ReadOnlySpan<byte> bytes, RagnarokRecordIndex index)
    {
        if (index.Preferences)
        {
            return new RagnarokGrammarEvidence(
                [new(RagnarokSpanKind.Opaque, 0, bytes.Length, 1, "Unknown")], [], [], FindCompression(bytes));
        }

        var spans = KnownSpans(bytes.Length, index);
        AddOpaqueGaps(spans, bytes.Length);
        return new RagnarokGrammarEvidence(
            spans.OrderBy(s => s.Offset).ToArray(),
            FindLengths(bytes, spans),
            FindHashes(bytes, index),
            FindCompression(bytes));
    }

    private static List<RagnarokSpan> KnownSpans(int fileLength, RagnarokRecordIndex index)
    {
        var spans = new List<RagnarokSpan>
        {
            new(RagnarokSpanKind.Header, 0, RagnarokFormat.HeaderSize, 1, "Observed"),
            new(RagnarokSpanKind.Metadata, RagnarokFormat.MetadataOffset, RagnarokFormat.MetadataSize, 1, "Observed"),
            new(RagnarokSpanKind.DirectoryHeader, RagnarokFormat.DirectoryMarkerOffset, 6, 1, "Observed"),
            new(RagnarokSpanKind.DirectoryIds, RagnarokFormat.DirectoryItemsOffset, index.DirectoryCount * sizeof(ulong), index.DirectoryCount, "Observed"),
            new(RagnarokSpanKind.PairedWords, index.FlagsOffset, index.DirectoryCount * sizeof(uint), index.DirectoryCount, "Observed correspondence; meaning unknown"),
            new(RagnarokSpanKind.SecondaryHeader, index.FlagsOffset + index.DirectoryCount * sizeof(uint), sizeof(uint), 1, "Observed"),
            new(RagnarokSpanKind.SecondaryIds, index.SecondaryOffset, index.SecondaryCount * sizeof(ulong), index.SecondaryCount, "Observed"),
            new(RagnarokSpanKind.WalletHeader, index.WalletOffset, RagnarokFormat.WalletHeaderSize, 1, "Bounded by observed marker/count"),
            new(RagnarokSpanKind.WalletRows, index.WalletOffset + RagnarokFormat.WalletHeaderSize,
                index.WalletCount * RagnarokFormat.WalletRowSize, index.WalletCount, "Bounded by observed wallet count"),
            new(RagnarokSpanKind.TrailingSentinel, fileLength - sizeof(uint), sizeof(uint), 1, "Observed sentinel; not a checksum"),
        };
        if (index.Nested.Wallets.Count > 0)
        {
            // Replace the hero-only spans: every byte belongs to exactly one evidence span.
            spans.RemoveAll(span => span.Kind is RagnarokSpanKind.WalletHeader or RagnarokSpanKind.WalletRows);
            spans.Add(new(RagnarokSpanKind.WalletBlock, index.Nested.WalletBlockOffset, sizeof(uint), 1, "Observed byte length"));
            foreach (var wallet in index.Nested.Wallets)
            {
                spans.Add(new(RagnarokSpanKind.WalletHeader, wallet.Offset, RagnarokFormat.WalletHeaderSize, 1, "Observed counts; role inferred"));
                spans.Add(new(RagnarokSpanKind.WalletRows, wallet.ResourcesOffset, wallet.ResourceCount * RagnarokFormat.WalletRowSize,
                    wallet.ResourceCount, "Counted resource rows; quantity meaning inferred"));
                spans.Add(new(RagnarokSpanKind.WalletIds, wallet.IdsOffset, wallet.IdCount * sizeof(ulong), wallet.IdCount, "Counted IDs; semantics unknown"));
                spans.Add(new(RagnarokSpanKind.WalletIndices, wallet.IndicesOffset, wallet.IndexCount * sizeof(uint), wallet.IndexCount, "Counted index-like words; target unknown"));
            }

            spans.Add(new(RagnarokSpanKind.NestedLinks, index.Nested.WalletBlockEnd,
                index.Nested.FlagsOffset - index.Nested.WalletBlockEnd, index.Nested.LinksCount, "u16 length and counted 22-byte rows; meaning unknown"));
            spans.Add(new(RagnarokSpanKind.FlagGroups, index.Nested.FlagsOffset,
                index.Nested.FlagsEnd - index.Nested.FlagsOffset, index.Nested.FlagGroups.Sum(g => g.Count), "Length-bounded groups of ID/four raw bytes; meaning unknown"));
        }
        return spans.Where(s => s.Offset >= 0 && s.Length > 0 && s.Offset <= fileLength - s.Length).ToList();
    }

    private static void AddOpaqueGaps(List<RagnarokSpan> spans, int fileLength)
    {
        var merged = spans.OrderBy(s => s.Offset).Aggregate(new List<(int Start, int End)>(), (ranges, span) =>
        {
            var end = span.Offset + span.Length;
            if (ranges.Count > 0 && span.Offset <= ranges[^1].End)
            {
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, end));
            }
            else
            {
                ranges.Add((span.Offset, end));
            }

            return ranges;
        });
        var cursor = 0;
        foreach (var (start, end) in merged)
        {
            if (start > cursor)
            {
                spans.Add(new(RagnarokSpanKind.Opaque, cursor, start - cursor, 1, "Unknown bytes preserved"));
            }

            cursor = Math.Max(cursor, end);
        }

        if (cursor < fileLength)
        {
            spans.Add(new(RagnarokSpanKind.Opaque, cursor, fileLength - cursor, 1, "Unknown bytes preserved"));
        }
    }

    private static IReadOnlyList<RagnarokLengthCandidate> FindLengths(ReadOnlySpan<byte> bytes, IReadOnlyList<RagnarokSpan> spans)
    {
        var result = new List<RagnarokLengthCandidate>();
        foreach (var span in spans.Where(s => s.Kind == RagnarokSpanKind.Opaque))
        {
            for (var offset = Align4(span.Offset); offset <= span.Offset + span.Length - sizeof(uint); offset += sizeof(uint))
            {
                var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
                if (value is > 0 and <= 1_048_576 && value <= bytes.Length - offset - sizeof(uint))
                {
                    result.Add(new(offset, value, "Opaque u32 followed by a fitting byte span; nested length unproven"));
                    if (result.Count == MaximumCandidates)
                    {
                        return result;
                    }
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<RagnarokHashCandidate> FindHashes(ReadOnlySpan<byte> bytes, RagnarokRecordIndex index)
    {
        var result = new List<RagnarokHashCandidate>(index.DirectoryCount + index.SecondaryCount + index.WalletCount);
        for (var row = 0; row < index.DirectoryCount; row++)
        {
            var offset = RagnarokFormat.DirectoryItemsOffset + row * sizeof(ulong);
            result.Add(new(offset, RagnarokFormat.UInt64(bytes, offset), "Sorted directory ID"));
        }

        for (var row = 0; row < index.SecondaryCount; row++)
        {
            var offset = index.SecondaryOffset + row * sizeof(ulong);
            result.Add(new(offset, RagnarokFormat.UInt64(bytes, offset), "Secondary directory ID"));
        }

        for (var row = 0; row < index.WalletCount; row++)
        {
            var offset = index.WalletOffset + RagnarokFormat.WalletHeaderSize + row * RagnarokFormat.WalletRowSize;
            result.Add(new(offset, RagnarokFormat.UInt64(bytes, offset), "Wallet resource ID"));
        }

        return result;
    }

    private static IReadOnlyList<RagnarokCompressionCandidate> FindCompression(ReadOnlySpan<byte> bytes)
    {
        var result = new List<RagnarokCompressionCandidate>();
        foreach (var (signature, label) in new (byte[] Signature, string Label)[]
        {
            ("LZ4"u8.ToArray(), "ASCII LZ4"), ("ZSTD"u8.ToArray(), "ASCII ZSTD"), ("Oodle"u8.ToArray(), "ASCII Oodle"),
            (new byte[] { 0x04, 0x22, 0x4D, 0x18 }, "LZ4 frame magic"),
            (new byte[] { 0x28, 0xB5, 0x2F, 0xFD }, "Zstandard frame magic"),
        })
        {
            var offset = bytes.IndexOf(signature);
            if (offset >= 0)
            {
                result.Add(new(offset, label, "Signature only; no decompressor or compressed coverage is established."));
            }
        }

        return result;
    }

    private static int Align4(int value) => (value + 3) & ~3;
}
