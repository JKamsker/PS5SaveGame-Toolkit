using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithExplorer(byte[] bytes, FaithContainer container)
{
    public const string FileBlobId = "faith/file";
    private const int WordsPerPage = 128;
    private const int MaximumChildren = 64;
    private const int MaximumBlobPage = 64 * 1024;

    public IReadOnlyList<ViewNode> BuildView() => [
        new TextNode("Typed blocks contain fixed structs and allocator arrays. Unknown payloads below are shown as " +
            "little-endian 32-bit words, not a recovered object schema. Raw values are read-only.") { Tone = Tone.Info },
        new StatsNode([
            new Stat("Container version", $"0x{container.Version:X16}"),
            new Stat("Trailer", $"0x{container.TrailerOffset:X} ({bytes.Length - container.TrailerOffset} bytes preserved)"),
        ]),
        BlockTable(),
        SectionNode.Lazy("Block payloads", "blocks", container.Blocks.Count.ToString(CultureInfo.InvariantCulture)),
    ];

    public IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        if (lazyId == "blocks")
        {
            return BlockGroups(0, container.Blocks.Count);
        }

        var parts = lazyId.Split('/');
        if (parts.Length == 3 && parts[0] == "block-range" && TryNumber(parts[1], out var first) &&
            TryNumber(parts[2], out var length) && first >= 0 && length > 0 && first <= container.Blocks.Count - length)
        {
            return BlockGroups(first, length);
        }

        if (parts.Length == 2 && parts[0] == "block" && TryBlock(parts[1], out var block))
        {
            return [
                new StatsNode([
                    new Stat("Type", block.Type.ToString(CultureInfo.InvariantCulture)),
                    new Stat("Version", block.Version.ToString(CultureInfo.InvariantCulture)),
                    new Stat("Header offset", $"0x{block.Offset:X}"),
                    new Stat("Payload", $"0x{block.PayloadOffset:X}, {block.PayloadLength} bytes"),
                ]),
                new HexNode($"block/{block.Index}", block.PayloadLength) { Title = block.Name },
                SectionNode.Lazy("Raw words (unknown meaning)", $"words/{block.Index}/0/{block.PayloadLength / sizeof(uint)}"),
            ];
        }

        if (parts.Length == 4 && parts[0] == "words" && TryBlock(parts[1], out block) &&
            TryNumber(parts[2], out var start) && TryNumber(parts[3], out var count) && start >= 0 && count >= 0 &&
            start <= block.PayloadLength / sizeof(uint) - count)
        {
            return Words(block, start, count);
        }

        throw new KeyNotFoundException($"Unknown Faith explorer node '{lazyId}'.");
    }

    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var start = 0;
        var length = bytes.Length;
        if (blobId != FileBlobId)
        {
            var parts = blobId.Split('/');
            if (parts.Length != 2 || parts[0] != "block" || !TryBlock(parts[1], out var block))
            {
                throw new KeyNotFoundException($"Unknown Faith blob '{blobId}'.");
            }

            start = block.PayloadOffset;
            length = block.PayloadLength;
        }

        if (offset < 0 || offset > length || count < 0 || count > MaximumBlobPage || count > length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Requested hex page is outside the blob or exceeds 64 KiB.");
        }

        return bytes.AsMemory(start + (int)offset, count);
    }

    private IReadOnlyList<ViewNode> BlockGroups(int first, int count)
    {
        var children = new List<ViewNode>();
        var groupSize = Math.Max(1, (count + MaximumChildren - 1) / MaximumChildren);
        for (var index = first; index < first + count; index += groupSize)
        {
            var length = Math.Min(groupSize, first + count - index);
            children.Add(length == 1
                ? SectionNode.Lazy($"{index}: {container.Blocks[index].Name}", $"block/{index}")
                : SectionNode.Lazy($"Blocks {index}–{index + length - 1}", $"block-range/{index}/{length}"));
        }

        return children;
    }

    private IReadOnlyList<ViewNode> Words(FaithBlock block, int start, int count)
    {
        if (count <= WordsPerPage)
        {
            var rows = new List<TableRow>(count);
            for (var word = start; word < start + count; word++)
            {
                var offset = block.PayloadOffset + word * sizeof(uint);
                var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
                rows.Add(new TableRow(offset.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>
                {
                    ["offset"] = new TextNode($"0x{offset:X8}") { Monospace = true },
                    ["hex"] = new TextNode($"0x{value:X8}") { Monospace = true },
                    ["uint"] = new TextNode(value.ToString(CultureInfo.InvariantCulture)),
                }));
            }

            return [new TableNode([new TableColumn("offset", "File offset"),
                new TableColumn("hex", "Unknown raw word"), new TableColumn("uint", "Unsigned interpretation")], rows)];
        }

        var groupSize = ((count + MaximumChildren * WordsPerPage - 1) / (MaximumChildren * WordsPerPage)) * WordsPerPage;
        var groups = new List<ViewNode>();
        for (var word = start; word < start + count; word += groupSize)
        {
            var length = Math.Min(groupSize, start + count - word);
            groups.Add(SectionNode.Lazy($"0x{block.PayloadOffset + word * sizeof(uint):X}–0x{block.PayloadOffset + (word + length) * sizeof(uint) - 1:X}",
                $"words/{block.Index}/{word}/{length}"));
        }

        return groups;
    }

    private TableNode BlockTable() => new([
        new TableColumn("index", "Index"), new TableColumn("type", "Type"), new TableColumn("name", "Research label"),
        new TableColumn("version", "Version"), new TableColumn("offset", "File offset"), new TableColumn("length", "Payload bytes"),
    ], container.Blocks.Select(block => new TableRow(block.Index.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>
    {
        ["index"] = new TextNode(block.Index.ToString(CultureInfo.InvariantCulture)),
        ["type"] = new TextNode(block.Type.ToString(CultureInfo.InvariantCulture)),
        ["name"] = new TextNode(block.Name),
        ["version"] = new TextNode(block.Version.ToString(CultureInfo.InvariantCulture)),
        ["offset"] = new TextNode($"0x{block.Offset:X}"),
        ["length"] = new TextNode(block.PayloadLength.ToString(CultureInfo.InvariantCulture)),
    })).ToArray()) { Title = "Block directory (duplicate types retained)" };

    private bool TryBlock(string text, out FaithBlock block)
    {
        block = null!;
        if (!TryNumber(text, out var index) || index < 0 || index >= container.Blocks.Count)
        {
            return false;
        }

        block = container.Blocks[index];
        return true;
    }

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
