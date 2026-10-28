using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed class RecordExplorer(byte[] bytes, IReadOnlyList<LinkedRecord> records, Func<ScalarField, FieldNode> bind)
{
    private const int WordSize = sizeof(uint);
    private const int WordsPerPage = 64;
    private const int BytesPerPage = WordsPerPage * WordSize;
    private const int MaximumChildren = 32;

    public IReadOnlyList<ViewNode> Record(int index)
    {
        var record = records[index];
        var fields = new List<ViewNode>
        {
            Number(record, 0, "Raw envelope +0x00", Confidence.Unknown),
            Number(record, LinkedRecords.NextPointerOffset,
                record.IsTail ? "Terminal word +0x04 (not followed)" : "Absolute next-record pointer",
                record.IsTail ? Confidence.Unknown : Confidence.Verified),
            Number(record, LinkedRecords.AllocationLengthOffset, "Raw envelope +0x08", Confidence.Unknown),
            Number(record, LinkedRecords.FlagsOffset, "Raw envelope +0x0C", Confidence.Unknown),
        };
        if (!record.IsTail)
        {
            fields.Add(new TextNode($"Tag: {record.Tag}; expected body starts at 0x{record.Offset + LinkedRecords.ContentOffset:X}.") { Monospace = true });
            fields.Add(Number(record, LinkedRecords.VersionOffset, "Version-like word +0x20", Confidence.Unknown));
            fields.Add(Number(record, LinkedRecords.DeclaredLengthOffset, "Declared content length (includes 24-byte content header)", Confidence.Verified));
            fields.Add(SectionNode.Lazy("All body words (u32 / i32 / f32 / ASCII)",
                WordId(index, LinkedRecords.ContentOffset, record.Length - LinkedRecords.ContentOffset)));
            if (record.IsEmpty)
            {
                fields.Add(new TextNode("Zero tag/length marks an empty slot; the allocated bytes remain available below.") { Tone = Tone.Info });
            }
        }

        fields.Add(new HexNode($"record/{index}", record.Length) { Title = "Complete record bytes (paged)" });
        return fields;
    }

    public IReadOnlyList<ViewNode> Words(int index, int offset, int length)
    {
        if (index < 0 || index >= records.Count || records[index].IsTail || offset < LinkedRecords.ContentOffset ||
            length <= 0 || offset % WordSize != 0 || length % WordSize != 0 || offset > records[index].Length - length)
        {
            throw new KeyNotFoundException("Unknown FFVII word range.");
        }

        if (length <= BytesPerPage)
        {
            return [WordTable(records[index], offset, length)];
        }

        var childLength = BytesPerPage;
        while ((length + childLength - 1) / childLength > MaximumChildren)
        {
            childLength *= MaximumChildren;
        }

        var children = new List<ViewNode>();
        for (var position = offset; position < offset + length; position += childLength)
        {
            var size = Math.Min(childLength, offset + length - position);
            children.Add(SectionNode.Lazy($"Record +0x{position:X}–0x{position + size - 1:X}", WordId(index, position, size)));
        }

        return children;
    }

    private TableNode WordTable(LinkedRecord record, int start, int length)
    {
        var rows = new List<TableRow>();
        for (var relative = start; relative < start + length; relative += WordSize)
        {
            var absolute = record.Offset + relative;
            var span = bytes.AsSpan(absolute, WordSize);
            var floating = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span));
            var ascii = Encoding.ASCII.GetString(span);
            var cells = new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["offset"] = new TextNode($"0x{absolute:X} (+0x{relative:X})") { Monospace = true },
                ["uint"] = Number(record, relative, $"Raw u32 +0x{relative:X}", Confidence.Unknown),
                ["int"] = new TextNode(BinaryPrimitives.ReadInt32LittleEndian(span).ToString()),
                ["float"] = new TextNode(floating.ToString("R")),
                ["ascii"] = new TextNode(new string([.. ascii.Select(c => c is >= ' ' and <= '~' ? c : '.')])) { Monospace = true },
            };
            rows.Add(new TableRow($"word/{absolute}", cells));
        }

        return new TableNode([new("offset", "Absolute (relative) offset"), new("uint", "u32 LE"), new("int", "i32 LE"),
            new("float", "f32 LE interpretation"), new("ascii", "ASCII")], rows)
        {
            Description = "Alternative interpretations of the same four bytes. Meanings and alignment are unknown; these are not Unreal property names.",
        };
    }

    private FieldNode Number(LinkedRecord record, int relative, string label, Confidence confidence) =>
        bind(new ScalarField($"raw/{record.Index}/{relative}", label, record.Offset + relative,
            IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }, confidence));

    private static string WordId(int record, int offset, int length) => $"words/{record}/{offset}/{length}";
}
