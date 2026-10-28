using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal static class RagnarokTables
{
    public static TableNode Regions(RagnarokRecordIndex index) => new(
        [new("name", "Structure"), new("offset", "Offset"), new("length", "Bytes"), new("count", "Rows"), new("meaning", "Limits")],
        index.Regions.Select(r => Row(r.Offset.ToString(), ("name", Text(r.Name)), ("offset", Offset(r.Offset)),
            ("length", Text(r.Length.ToString())), ("count", Text(r.Count.ToString())), ("meaning", Text(r.Meaning)))).ToArray())
        { Title = "Established spans (partial index)" };

    public static TableNode Evidence(RagnarokRecordIndex index)
    {
        var rows = index.Evidence.Spans.Select(span => Row($"span/{span.Offset:X}",
            ("kind", Text(span.Kind.ToString())), ("offset", Offset(span.Offset)),
            ("length", Text(span.Length.ToString())), ("count", Text(span.Count.ToString())),
            ("confidence", Text(span.Confidence))));
        return new([new("kind", "Span"), new("offset", "Offset"), new("length", "Bytes"),
            new("count", "Count"), new("confidence", "Evidence")], rows.ToArray())
        { Title = $"Grammar spans ({index.Evidence.KnownBytes:N0} known, {index.Evidence.OpaqueBytes:N0} opaque bytes)" };
    }

    public static TableNode Records(RagnarokDocument document, RagnarokRecordIndex index, RagnarokReferences references, string kind, int start, int count)
    {
        var rows = Enumerable.Range(start, count).Select(row => kind switch
        {
            "directory" => DirectoryRow(document, index, row),
            "secondary" => IdRow(document, index.SecondaryOffset + row * sizeof(ulong)),
            "wallet" => WalletRow(document, references, index.WalletOffset + RagnarokFormat.WalletHeaderSize + row * RagnarokFormat.WalletRowSize),
            "anchors" => AnchorRow(document, references, index, index.Occurrences[row]),
            _ => throw new KeyNotFoundException(kind),
        }).ToArray();
        IReadOnlyList<TableColumn> columns = kind switch
        {
            "directory" => [new("offset", "Offset"), new("id", "ID (LE u64)"), new("word", "Paired word (unknown)")],
            "secondary" => [new("offset", "Offset"), new("id", "ID (LE u64)")],
            "wallet" => [new("offset", "Offset"), new("name", "Resource"), new("id", "ID (LE u64)"), new("quantity", "Quantity (inferred)"), new("word", "Metadata (unknown)")],
            _ => [new("offset", "Offset"), new("name", "Source name"), new("id", "ID (LE u64)"), new("context", "Context"), new("quantity", "Word at +8")],
        };
        return new(columns, rows) { Title = kind };
    }

    public static TableNode Words(RagnarokDocument document, int offset, int length, bool byteValues)
    {
        var rows = new List<TableRow>();
        var end = offset + length;
        for (var position = offset; position < end;)
        {
            var width = byteValues || end - position < sizeof(uint) ? 1 : sizeof(uint);
            rows.Add(Row(position.ToString(), ("offset", Offset(position)),
                ("value", document.Number(position, $"0x{position:X}", Confidence.Unknown, width: width))));
            position += width;
        }

        return new([new("offset", "Offset"), new("value", byteValues ? "Raw setting byte (unknown)" : "Raw LE word (unknown)")], rows);
    }

    private static TableRow DirectoryRow(RagnarokDocument document, RagnarokRecordIndex index, int row)
    {
        var offset = RagnarokFormat.DirectoryItemsOffset + row * sizeof(ulong);
        return Row(offset.ToString(), ("offset", Offset(offset)), ("id", Id(document, offset)),
            ("word", document.Number(index.FlagsOffset + row * sizeof(uint), "Paired word", Confidence.Unknown)));
    }

    private static TableRow IdRow(RagnarokDocument document, int offset) =>
        Row(offset.ToString(), ("offset", Offset(offset)), ("id", Id(document, offset)));

    private static TableRow WalletRow(RagnarokDocument document, RagnarokReferences references, int offset) => Row(offset.ToString(),
        ("offset", Offset(offset)), ("name", Text(references.Name(document.ReadId(offset)))), ("id", Id(document, offset)),
        ("quantity", document.Number(offset + sizeof(ulong), "Quantity (wallet role inferred)", Confidence.Inferred)
            with { Warning = "Container role transferred from PC research; no PS5 game UI or integrity validation." }),
        ("word", document.Number(offset + RagnarokFormat.WalletRowMetadataOffset, "Metadata", Confidence.Unknown)));

    private static TableRow AnchorRow(RagnarokDocument document, RagnarokReferences references, RagnarokRecordIndex index, ResourceOccurrence occurrence)
    {
        var offset = occurrence.Offset;
        var start = index.WalletOffset + RagnarokFormat.WalletHeaderSize;
        var inWallet = offset >= start && offset < start + index.WalletCount * RagnarokFormat.WalletRowSize &&
            (offset - start) % RagnarokFormat.WalletRowSize == 0;
        return Row(offset.ToString(), ("offset", Offset(offset)), ("name", Text(references.Name(occurrence.Id))),
            ("id", Id(document, offset)), ("context", Text(inWallet ? "Hero wallet (inferred)" : "Outside wallet; unknown")),
            ("quantity", document.Number(offset + sizeof(ulong), inWallet ? "Quantity (wallet role inferred)" : "Word at +8",
                inWallet ? Confidence.Inferred : Confidence.Unknown)
                with { Warning = "Duplicate resource IDs are separate occurrences; these values are never added together." }));
    }

    private static FieldNode Id(RagnarokDocument document, int offset) =>
        document.Number(offset, "ID", Confidence.Unknown, hex: true, width: sizeof(ulong));

    private static TextNode Text(string text) => new(text);
    private static TextNode Offset(int offset) => new($"0x{offset:X}") { Monospace = true };
    private static TableRow Row(string key, params (string Key, ViewNode Value)[] cells) =>
        new(key, cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal));
}
