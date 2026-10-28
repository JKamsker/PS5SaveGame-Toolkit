using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed class RagnarokDocument(string path, string name, byte[] bytes, RagnarokRecordIndex index) : BoundSaveDocument
{
    private const string BlobId = "save";
    internal const int RowsPerPage = 256;
    internal const int RawPageSize = 1024;
    internal const int RawGroupSize = 64 * 1024;
    private int PageSize => index.Preferences ? RowsPerPage : RawPageSize;

    public override string Title => index.Preferences ? "Ragnarök · User preferences" : $"Ragnarök · {name}";
    public override bool CanEdit => false;

    public override ViewNode BuildView()
    {
        var overview = new List<ViewNode>
        {
            new TextNode("Read-only research view. Integrity and the full record grammar are unresolved; editing is disabled.") { Tone = Tone.Warning },
            new StatsNode([
                new("File", name), new("Bytes", bytes.Length.ToString()),
                new("Indexed IDs", index.DirectoryCount.ToString()), new("Wallet rows", index.WalletCount.ToString()),
            ]),
            Number(0, "Timestamp-like word (inferred Unix seconds)", Confidence.Inferred),
            Number(RagnarokFormat.KindOffset, "Record kind word", Confidence.Unknown, hex: true),
            Number(RagnarokFormat.RevisionOffset, "Revision word", Confidence.Unknown, hex: true),
            SectionNode.Lazy("All outer header words", "header"),
        };
        if (index.Preferences)
        {
            overview.Add(new TextNode("696-byte settings record. Individual settings, ranges and integrity are unknown; byte labels are raw offsets."));
            return new TabsNode([new("Preferences", overview), new("Raw settings", RawRoot())]);
        }

        overview.Add(Field("metadata/location", "Location / quest description", new TextSpec(), () => FieldValue.Of(index.Description))
            with { Confidence = Confidence.Verified, Description = "NUL-terminated UTF-8 text present in the save metadata." });
        overview.Add(Field("metadata/play-seconds", "Play time (inferred)",
            IntegerSpec.UInt32 with { Format = IntegerFormat.Duration, UnitsPerSecond = 1 },
            () => FieldValue.Of((long)ReadWord(RagnarokFormat.PlaySecondsOffset)))
            with { Confidence = Confidence.Inferred,
                Description = "PC reader identifies seconds here; PS5 values are consistent, but not compared with the game UI." });
        overview.Add(Number(RagnarokFormat.SaveCounterOffset, "Save counter (inferred)", Confidence.Inferred));
        return new TabsNode([
            new("Overview", overview),
            new("Wallet", [new TextNode("Hero-wallet role is inferred from the PC layout. Source-matched resource names are cited in the README; unnamed IDs stay raw."),
                SectionNode.Lazy("Wallet records", "wallet", index.WalletCount.ToString())]),
            new("Record index", [RagnarokTables.Regions(index),
                new TextNode("Partial index only. Gaps, nesting, lengths outside these arrays and integrity dependencies remain unknown.") { Tone = Tone.Warning },
                SectionNode.Lazy("Sorted IDs and paired words", "directory", index.DirectoryCount.ToString()),
                SectionNode.Lazy("Secondary ID list", "secondary", index.SecondaryCount.ToString())]),
            new("Resource anchors", [new TextNode("All occurrences of cited resource IDs, including duplicates outside the wallet. Adjacent words outside wallet rows are unknown."),
                SectionNode.Lazy("Resource occurrences", "anchors", index.Occurrences.Count.ToString())]),
            new("Raw", RawRoot()),
        ]);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId == "header")
        {
            return [RagnarokTables.Words(this, 0, RagnarokFormat.HeaderSize, false)];
        }

        if (lazyId == "raw")
        {
            return Enumerable.Range(0, (bytes.Length + RawGroupSize - 1) / RawGroupSize)
                .Select(group => (ViewNode)SectionNode.Lazy($"Bytes 0x{group * RawGroupSize:X}", $"raw-group/{group}")).ToArray();
        }

        if (TryPage(lazyId, "raw-group", (bytes.Length + RawGroupSize - 1) / RawGroupSize, out var rawGroup))
        {
            var start = rawGroup * RawGroupSize;
            var end = Math.Min(start + RawGroupSize, bytes.Length);
            return Enumerable.Range(start / PageSize, (end - start + PageSize - 1) / PageSize)
                .Select(page => (ViewNode)SectionNode.Lazy($"Bytes 0x{page * PageSize:X}", $"raw-page/{page}")).ToArray();
        }

        if (TryPage(lazyId, "raw-page", (bytes.Length + PageSize - 1) / PageSize, out var rawPage))
        {
            var start = rawPage * PageSize;
            return [RagnarokTables.Words(this, start, Math.Min(PageSize, bytes.Length - start), index.Preferences)];
        }

        if (!index.Preferences)
        {
            foreach (var (kind, count) in new[] { ("directory", index.DirectoryCount), ("secondary", index.SecondaryCount),
                ("wallet", index.WalletCount), ("anchors", index.Occurrences.Count) })
            {
                if (lazyId == kind)
                {
                    return Enumerable.Range(0, (count + RowsPerPage - 1) / RowsPerPage)
                        .Select(page => (ViewNode)SectionNode.Lazy($"Rows {page * RowsPerPage}–{Math.Min(count, (page + 1) * RowsPerPage) - 1}", $"{kind}/{page}"))
                        .ToArray();
                }

                if (TryPage(lazyId, kind, (count + RowsPerPage - 1) / RowsPerPage, out var page))
                {
                    return [RagnarokTables.Records(this, index, kind, page * RowsPerPage, Math.Min(RowsPerPage, count - page * RowsPerPage))];
                }
            }
        }

        throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        _ = RagnarokRecordIndex.Parse(bytes, index.Preferences);
        return [new(path, bytes.ToArray())];
    }

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        if (blobId != BlobId)
        {
            throw new KeyNotFoundException($"Unknown blob '{blobId}'.");
        }

        if (offset < 0 || offset > bytes.Length || count < 0 || count > RawGroupSize || count > bytes.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Byte pages must fit the save and contain at most 64 KiB.");
        }

        // Return a copy so a caller cannot mutate the document's original bytes.
        return bytes.AsSpan((int)offset, count).ToArray();
    }

    internal uint ReadWord(int offset) => RagnarokFormat.UInt32(bytes, offset);
    internal ulong ReadId(int offset) => RagnarokFormat.UInt64(bytes, offset);

    internal FieldNode Number(int offset, string label, Confidence confidence, bool hex = false, int width = sizeof(uint))
    {
        var spec = width == sizeof(ulong) ? IntegerSpec.UInt64 : width == 1 ? IntegerSpec.UInt8 : IntegerSpec.UInt32;
        return Field($"raw/{offset:X}/{width}/{(hex ? "hex" : "decimal")}", label, spec with { Format = hex ? IntegerFormat.Hex : IntegerFormat.Decimal },
            () => FieldValue.Of(width == sizeof(ulong) ? ReadId(offset) : width == 1 ? bytes[offset] : ReadWord(offset)))
            with { Confidence = confidence, Description = $"Little-endian bytes at 0x{offset:X} ({width} bytes)." };
    }

    private IReadOnlyList<ViewNode> RawRoot() =>
    [
        new HexNode(BlobId, bytes.Length) { Title = "Original bytes" },
        SectionNode.Lazy(index.Preferences ? "Raw setting bytes" : "Raw word interpretations", "raw"),
    ];

    private static bool TryPage(string id, string prefix, int pageCount, out int page)
    {
        page = -1;
        return id.StartsWith(prefix + "/", StringComparison.Ordinal) &&
            int.TryParse(id.AsSpan(prefix.Length + 1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out page) && page >= 0 && page < pageCount;
    }
}
