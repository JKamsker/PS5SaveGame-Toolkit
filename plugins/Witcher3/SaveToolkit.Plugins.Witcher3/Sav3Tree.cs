using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record Sav3Node(int Id, int Offset, int Size, string Tag, string Name, string? Type, int ValueOffset, int ValueSize)
{
    public List<int> Children { get; } = [];
}

internal sealed class Sav3Tree
{
    private const int MaximumNodes = 200_000;
    private const int MaximumDepth = 128;
    private readonly byte[] _data;
    private readonly IReadOnlyList<string> _names;

    private Sav3Tree(byte[] data, IReadOnlyList<string> names)
    {
        _data = data;
        _names = names;
    }

    public List<Sav3Node> Nodes { get; } = [];
    public List<int> Roots { get; } = [];

    public static Sav3Tree Parse(byte[] data, Sav3Directory directory)
    {
        var result = new Sav3Tree(data, directory.Names);
        var parents = new Stack<Sav3Node>();
        var archives = new List<(Sav3Node Node, int Depth)>();
        var previousOffset = -1;
        foreach (var entry in directory.Entries)
        {
            while (parents.TryPeek(out var candidate) && entry.Offset >= candidate.Offset + candidate.Size)
            {
                parents.Pop();
            }

            if (entry.Offset <= previousOffset || parents.Count > 0 && entry.Offset + entry.Size > parents.Peek().Offset + parents.Peek().Size)
            {
                throw new SaveFormatException("SAV3 variable spans cross or have duplicate offsets.");
            }

            if (parents.Count >= MaximumDepth)
            {
                throw new SaveFormatException("SAV3 variable nesting exceeds the supported limit.");
            }

            if (parents.TryPeek(out var parent) && parent.Tag == "SS")
            {
                throw new SaveFormatException("SAV3 SS archive contains an unsupported indexed child.");
            }

            var node = result.Add(entry.Offset, entry.Size);
            (parents.Count > 0 ? parents.Peek().Children : result.Roots).Add(node.Id);
            if (node.Tag == "SS") { archives.Add((node, parents.Count + 1)); }
            parents.Push(node);
            previousOffset = entry.Offset;
        }

        // Indexed BS spans already describe their children. SS wraps a separate, non-indexed archive.
        foreach (var (node, depth) in archives)
        {
            result.ReadSequence(node.Offset + 6, node.Offset + node.Size, node.Children, depth);
        }

        return result;
    }

    private void ReadSequence(int start, int end, List<int> children, int depth)
    {
        if (start < end && depth >= MaximumDepth)
        {
            throw new SaveFormatException("SAV3 embedded archive exceeds the supported nesting limit.");
        }

        var position = start;
        while (position < end)
        {
            var remaining = _data.AsSpan(position, end - position);
            var size = remaining.Length;
            var childStart = -1;
            var opaque = false;
            if (remaining.StartsWith("SXAP"u8))
            {
                size = 16;
            }
            else if (remaining.StartsWith("BLCK"u8))
            {
                const int headerSize = 10;
                var innerSize = BinaryData.Int32(remaining, 6);
                size = BoundedSize(innerSize, headerSize, remaining.Length);
                childStart = position + headerSize;
            }
            else if (remaining.StartsWith("PORP"u8) || remaining.StartsWith("AVAL"u8))
            {
                size = BoundedSize(BinaryData.Int32(remaining, 8), 12, remaining.Length);
            }
            else
            {
                opaque = true;
            }
            // Unsupported tokens remain an opaque remainder of this bounded parent.
            if (size > remaining.Length)
            {
                throw new SaveFormatException("SAV3 archive token is truncated.");
            }

            var node = Add(position, size, opaque);
            children.Add(node.Id);
            if (childStart >= 0)
            {
                ReadSequence(childStart, position + size, node.Children, depth + 1);
            }

            position += size;
        }
    }

    private static int BoundedSize(int size, int header, int available)
    {
        if (size < 0 || size > available - header)
        {
            throw new SaveFormatException("SAV3 embedded token length exceeds its parent.");
        }

        return header + size;
    }

    private Sav3Node Add(int offset, int size, bool opaque = false)
    {
        if (Nodes.Count >= MaximumNodes)
        {
            throw new SaveFormatException("SAV3 explorer exceeds the supported node limit.");
        }

        var raw = BinaryData.Slice(_data, offset, size);
        if (opaque)
        {
            raw = [];
        }

        var tag = "Opaque";
        string? name = null;
        string? type = null;
        var valueStart = 0;
        if (raw.StartsWith("BS"u8))
        {
            tag = "BS";
            name = Name(BinaryData.UInt16(raw, 2));
            valueStart = 4;
        }
        else if (raw.StartsWith("VL"u8) || raw.StartsWith("OP"u8))
        {
            tag = raw.StartsWith("VL"u8) ? "VL" : "OP";
            name = Name(BinaryData.UInt16(raw, 2));
            type = Name(BinaryData.UInt16(raw, 4));
            valueStart = 6;
        }
        else if (raw.StartsWith("PORP"u8) || raw.StartsWith("AVAL"u8))
        {
            tag = raw.StartsWith("PORP"u8) ? "PORP" : "AVAL";
            name = Name(BinaryData.UInt16(raw, 4));
            type = Name(BinaryData.UInt16(raw, 6));
            valueStart = 12;
        }
        else if (raw.StartsWith("BLCK"u8))
        {
            tag = "BLCK";
            name = Name(BinaryData.UInt16(raw, 4));
            valueStart = 10;
        }
        else if (raw.StartsWith("SS"u8))
        {
            tag = "SS";
            name = "Embedded archive";
            valueStart = 6;
        }
        else if (raw.StartsWith("SXAP"u8) || raw.StartsWith("MANU"u8))
        {
            tag = raw.StartsWith("SXAP"u8) ? "SXAP" : "MANU";
            name = tag == "MANU" ? "Name table" : "Archive version header";
            if (tag == "SXAP")
            {
                _ = BinaryData.Slice(raw, 0, 16);
            }
        }

        _ = BinaryData.Slice(raw, 0, opaque ? 0 : valueStart);
        if ((tag is "SS" or "BLCK" or "PORP" or "AVAL") && BinaryData.Int32(raw, valueStart - sizeof(int)) != size - valueStart)
        {
            throw new SaveFormatException($"SAV3 {tag} token length disagrees with its indexed or embedded span.");
        }

        var node = new Sav3Node(Nodes.Count, offset, size, tag, name ?? $"Bytes at 0x{offset:X}", type, offset + valueStart, size - valueStart);
        Nodes.Add(node);
        return node;
    }

    private string Name(int index)
    {
        if (index < 1 || index > _names.Count)
        {
            throw new SaveFormatException("SAV3 variable references an invalid MANU name index.");
        }

        return _names[index - 1];
    }
}
