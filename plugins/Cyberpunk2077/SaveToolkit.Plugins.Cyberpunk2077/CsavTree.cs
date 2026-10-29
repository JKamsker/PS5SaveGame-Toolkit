using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record CsavNode(int Id, string Name, int NextId, int ChildId, int Offset, int Size)
{
    public List<int> Children { get; } = [];
}

internal sealed record CsavTree(IReadOnlyList<CsavNode> Nodes, IReadOnlyList<int> Roots)
{
    public static CsavTree Parse(CsavContainer container)
    {
        var directory = container.Original.AsSpan(container.DirectoryOffset, container.Original.Length - container.DirectoryOffset - 8);
        var position = 4;
        var count = BinaryData.PackedInt(directory, ref position);
        const int maxNodes = 100_000;
        if (count is < 1 or > maxNodes || count > (directory.Length - position) / 17)
        {
            throw new SaveFormatException("Invalid CSAV node count.");
        }

        var nodes = new CsavNode[count];
        for (var index = 0; index < count; index++)
        {
            var name = BinaryData.PackedName(directory, ref position);
            var next = BinaryData.Int32(directory, position);
            var child = BinaryData.Int32(directory, position + 4);
            var offset = BinaryData.Int32(directory, position + 8) - container.DataOffset;
            var size = BinaryData.Int32(directory, position + 12);
            position += 16;
            if (next != -1 && (next <= index || next >= count) ||
                child != -1 && child != index + 1 || child >= count || size < sizeof(int))
            {
                throw new SaveFormatException($"Invalid CSAV node {index} links or size.");
            }

            var data = BinaryData.Slice(container.Payload, offset, size);
            if (BinaryData.Int32(data, 0) != index || index > 0 && offset < nodes[index - 1].Offset)
            {
                throw new SaveFormatException($"CSAV node {index} ID or ordering disagrees with its directory.");
            }

            nodes[index] = new CsavNode(index, name, next, child, offset, size);
        }

        // Unknown bytes after the directory entries are retained by the container writer.
        var roots = new List<int>();
        var parents = new Stack<(CsavNode Node, int Limit)>();
        for (var index = 0; index < count; index++)
        {
            while (parents.TryPeek(out var parent) && index >= parent.Limit)
            {
                parents.Pop();
            }

            var node = nodes[index];
            var limit = parents.TryPeek(out var current) ? current.Limit : count;
            var siblings = parents.Count > 0 ? current.Node.Children : roots;
            siblings.Add(index);
            if (parents.Count > 0 && (node.Offset < current.Node.Offset + sizeof(int) ||
                node.Offset + node.Size > current.Node.Offset + current.Node.Size))
            {
                throw new SaveFormatException("CSAV child extends beyond its parent node.");
            }

            if (node.NextId >= limit)
            {
                throw new SaveFormatException("CSAV sibling link crosses its parent boundary.");
            }

            if (node.ChildId >= 0)
            {
                var childLimit = node.NextId < 0 ? limit : node.NextId;
                if (node.ChildId >= childLimit)
                {
                    throw new SaveFormatException($"CSAV node {index} child link crosses its sibling or parent boundary.");
                }

                const int maxDepth = 128;
                if (parents.Count >= maxDepth)
                {
                    throw new SaveFormatException("CSAV tree nesting exceeds the supported limit.");
                }

                parents.Push((node, childLimit));
            }
        }

        ValidateSiblings(roots, nodes);
        foreach (var node in nodes)
        {
            ValidateSiblings(node.Children, nodes);
        }

        return new CsavTree(nodes, roots);
    }

    private static void ValidateSiblings(IReadOnlyList<int> siblings, CsavNode[] nodes)
    {
        for (var index = 0; index < siblings.Count; index++)
        {
            var expected = index + 1 < siblings.Count ? siblings[index + 1] : -1;
            if (nodes[siblings[index]].NextId != expected)
            {
                throw new SaveFormatException("CSAV sibling chain disagrees with its child links.");
            }

            var node = nodes[siblings[index]];
            if (expected >= 0 && node.Offset + node.Size > nodes[expected].Offset)
            {
                throw new SaveFormatException("CSAV sibling node ranges overlap.");
            }
        }
    }
}
