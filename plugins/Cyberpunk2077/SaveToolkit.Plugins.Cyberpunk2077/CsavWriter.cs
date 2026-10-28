using System.Buffers.Binary;
using K4os.Compression.LZ4;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal static class CsavWriter
{
    private const int TableOffset = 33;
    private const int DescriptorSize = 12;

    public static byte[] Rebuild(CsavContainer container, byte[] edited, KeyValuePair<int, byte[]>? replacement)
    {
        var tree = CsavTree.Parse(container);
        var payload = edited;
        var directory = container.Original.AsSpan(container.DirectoryOffset).ToArray();
        CsavNode? replaced = null;
        var delta = 0;
        if (replacement is { } pair)
        {
            replaced = tree.Nodes[pair.Key];
            if (replaced.Children.Count != 0 || pair.Value.Length < 4 || BinaryData.Int32(pair.Value, 0) != replaced.Id)
            {
                throw new SaveFormatException("Only complete leaf-node replacements with matching IDs are supported.");
            }

            delta = pair.Value.Length - replaced.Size;
            if (edited.Length + (long)delta > BinaryData.MaxExpandedSize)
            {
                throw new SaveFormatException("Edited CSAV exceeds the expanded data limit.");
            }

            payload = new byte[checked(edited.Length + delta)];
            edited.AsSpan(0, replaced.Offset).CopyTo(payload);
            pair.Value.CopyTo(payload, replaced.Offset);
            edited.AsSpan(replaced.Offset + replaced.Size).CopyTo(payload.AsSpan(replaced.Offset + pair.Value.Length));
            UpdateDirectory(directory, tree, replaced, delta, container.DataOffset);
        }

        if (payload.AsSpan().SequenceEqual(container.Payload))
        {
            return container.Original.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(container.Original.AsSpan(0, container.DataOffset));
        var descriptors = new List<(int Offset, int Stored, int Expanded)>();
        Span<byte> integer = stackalloc byte[4];
        foreach (var chunk in container.Chunks)
        {
            var start = MapOffset(chunk.PayloadOffset, replaced, delta);
            var end = MapOffset(chunk.PayloadOffset + chunk.ExpandedSize, replaced, delta);
            var content = payload.AsSpan(start, end - start);
            var offset = checked((int)output.Position);
            if (content.SequenceEqual(container.Payload.AsSpan(chunk.PayloadOffset, chunk.ExpandedSize)))
            {
                output.Write(container.Original.AsSpan(chunk.Offset, chunk.StoredSize));
            }
            else if (!chunk.Compressed)
            {
                output.Write(content);
            }
            else
            {
                var buffer = new byte[LZ4Codec.MaximumOutputSize(content.Length)];
                var length = LZ4Codec.Encode(content, buffer);
                if (length <= 0)
                {
                    throw new SaveFormatException("Could not encode the edited CSAV LZ4 block.");
                }

                output.Write("4ZLX"u8);
                BinaryPrimitives.WriteInt32LittleEndian(integer, content.Length);
                output.Write(integer);
                output.Write(buffer.AsSpan(0, length));
            }

            descriptors.Add((offset, checked((int)output.Position) - offset, content.Length));
            var nextIndex = descriptors.Count;
            var gapEnd = nextIndex < container.Chunks.Count ? container.Chunks[nextIndex].Offset : container.DirectoryOffset;
            var originalEnd = chunk.Offset + chunk.StoredSize;
            output.Write(container.Original.AsSpan(originalEnd, gapEnd - originalEnd));
        }

        var directoryOffset = checked((int)output.Position);
        output.Write(directory);
        var result = output.ToArray();
        for (var index = 0; index < descriptors.Count; index++)
        {
            var descriptor = result.AsSpan(TableOffset + index * DescriptorSize);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor, descriptors[index].Offset);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[4..], descriptors[index].Stored);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[8..], descriptors[index].Expanded);
        }

        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(result.Length - 8), directoryOffset);
        var reopened = CsavContainer.Parse(result);
        if (!reopened.Payload.AsSpan().SequenceEqual(payload))
        {
            throw new SaveFormatException("Edited CSAV failed decompression verification.");
        }

        var reopenedTree = CsavTree.Parse(reopened);
        _ = InventoryReader.Read(reopened, reopenedTree);
        foreach (var node in reopenedTree.Nodes.Where(n => n.Name == "FactsTable"))
        {
            _ = FactsReader.Read(BinaryData.Slice(reopened.Payload, node.Offset, node.Size));
        }

        return result;
    }

    private static int MapOffset(int offset, CsavNode? node, int delta)
    {
        if (node is null || offset <= node.Offset)
        {
            return offset;
        }

        return offset >= node.Offset + node.Size ? offset + delta : node.Offset + Math.Min(offset - node.Offset, node.Size + delta);
    }

    private static void UpdateDirectory(byte[] bytes, CsavTree tree, CsavNode replaced, int delta, int dataOffset)
    {
        var position = 4;
        _ = BinaryData.PackedInt(bytes, ref position);
        foreach (var node in tree.Nodes)
        {
            _ = BinaryData.PackedName(bytes, ref position);
            var offset = node.Offset >= replaced.Offset + replaced.Size ? node.Offset + delta : node.Offset;
            var size = node.Offset <= replaced.Offset && node.Offset + node.Size >= replaced.Offset + replaced.Size
                ? node.Size + delta : node.Size;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(position + 8), offset + dataOffset);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(position + 12), size);
            position += 16;
        }
    }
}
