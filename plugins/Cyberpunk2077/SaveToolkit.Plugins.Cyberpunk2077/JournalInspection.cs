using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class JournalInspection
{
    public const string CollectionId = "journal";
    private const int EntrySize = 15;
    private const string Source = "WolvenKit JournalManagerParser, GPL-3.0, format reference only; " +
        "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Save/Parser/JournalManagerParser.cs";
    public JournalInspection(CsavContainer container, CsavTree tree)
    {
        var rows = new List<InspectionRow>();
        try
        {
            if (!InventoryReader.IsVerifiedVersion(container)) throw new SaveFormatException("Journal schema version is unverified.");
            var nodes = tree.Nodes.Where(n => n.Name == "JournalManager").ToArray();
            if (nodes.Length != 1) throw new SaveFormatException("No unique journal node.");
            var node = nodes[0];
            var raw = BinaryData.Slice(container.Payload, node.Offset, node.Size);
            var count = BinaryData.Int32(raw, 4);
            if (count < 0 || count > (raw.Length - 8) / EntrySize) throw new SaveFormatException("Journal record count exceeds its node.");
            for (var index = 0; index < count; index++) rows.Add(new InspectionRow($"journal/{node.Id}/{index}", new Dictionary<string, string>
            {
                ["index"] = index.ToString(CultureInfo.InvariantCulture), ["node"] = node.Id.ToString(CultureInfo.InvariantCulture),
                ["bytes"] = Convert.ToHexString(raw.Slice(8 + index * EntrySize, EntrySize)),
            }, Source, Confidence.Unknown));
            var position = 8 + count * EntrySize;
            var tracked = BinaryData.UInt32(raw, position);
            position += 4;
            var plane64 = SkipPlane(raw, ref position, 8);
            var plane128 = SkipPlane(raw, ref position, 16);
            Summary = $"{count} opaque 15-byte journal records; tracked-path candidate 0x{tracked:X8} (Unknown). " +
                $"Additional opaque planes: {plane64} × 8 bytes, {plane128} × 16 bytes; retained suffix {raw.Length - position} bytes. " +
                "Journal identities are not fact hashes; no quest-path, stage or completion mapping is proven.";
        }
        catch (SaveFormatException ex) { rows.Clear(); Summary = ex.Message; }
        Table = new InspectionTable(CollectionId, "Journal record diagnostics (read-only)", [new("index", "Record index", IntegerSpec.UInt32),
            new("node", "Node", IntegerSpec.UInt32), new("bytes", "Opaque saved bytes")], rows, Summary + " " + Source);
    }
    public string Summary { get; }
    public InspectionTable Table { get; }
    public IReadOnlyList<ViewNode> Build() => [new TextNode(Summary) { Tone = Tone.Warning }, Table.View()];

    private static int SkipPlane(ReadOnlySpan<byte> raw, ref int position, int stride)
    {
        var count = BinaryData.Int32(raw, position);
        position += 4;
        if (count < 0 || count > (raw.Length - position) / stride) throw new SaveFormatException("Journal plane exceeds its node.");
        position += count * stride;
        return count;
    }
}
