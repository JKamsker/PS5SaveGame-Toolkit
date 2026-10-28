using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record Sav3Patch(int Offset, int Length, byte[] Bytes)
{
    public int End => Offset + Length;
    public int Growth => Bytes.Length - Length;
}

/// <summary>Splices bounded values into the original image, relocating only known directories.</summary>
internal static class Sav3Writer
{
    public static byte[] Write(WitcherContainer container, Sav3Directory directory, Sav3Tree tree, IEnumerable<Sav3Patch> edits)
    {
        var original = container.Payload;
        var patches = edits.OrderBy(p => p.Offset).ToArray();
        var table = directory.VariableTableOffset;
        var rb = BinaryData.PayloadOffset(original, table - 6, container.HeaderSize);
        var previousEnd = 16;
        foreach (var patch in patches)
        {
            if (patch.Offset < previousEnd || patch.Length < 1 || patch.End > table - 10)
            {
                throw new SaveFormatException("SAV3 edits overlap or exceed the variable region.");
            }

            previousEnd = patch.End;
        }

        var size = (long)original.Length + patches.Sum(p => (long)p.Growth);
        if (size < 32 || size > BinaryData.MaxExpandedSize)
        {
            throw new SaveFormatException("Edited SAV3 body exceeds the supported size limit.");
        }

        var data = original.ToArray();
        foreach (var node in tree.Nodes)
        {
            var growth = GrowthInside(node.Offset, node.Size);
            if (growth == 0 || patches.Any(p => p.Offset == node.Offset && p.Length == node.Size))
            {
                continue;
            }

            var lengthOffset = node.Tag switch { "SS" => 2, "BLCK" => 6, "PORP" or "AVAL" => 8, _ => -1 };
            if (lengthOffset >= 0)
            {
                Put(node.Offset + lengthOffset, BinaryData.Int32(original, node.Offset + lengthOffset) + growth);
            }
        }

        // Iterate the stored directory, not the sorted explorer: table order is opaque and preserved.
        for (var index = 0; index < directory.Entries.Count; index++)
        {
            var entry = table + 4 + index * 8;
            var offset = BinaryData.PayloadOffset(original, entry, container.HeaderSize);
            var length = BinaryData.Int32(original, entry + 4);
            Put(entry, Relocate(offset) + container.HeaderSize);
            Put(entry + 4, length + GrowthInside(offset, length));
        }

        var rbCount = BinaryData.Int32(original, rb + 2);
        for (var index = 0; index < rbCount; index++)
        {
            var entry = rb + 6 + index * 6 + 2;
            Put(entry, Relocate(BinaryData.PayloadOffset(original, entry, container.HeaderSize)) + container.HeaderSize);
        }

        foreach (var pointer in new[] { table - 10, table - 6, original.Length - 6 })
        {
            Put(pointer, Relocate(BinaryData.PayloadOffset(original, pointer, container.HeaderSize)) + container.HeaderSize);
        }

        using var output = new MemoryStream((int)size);
        var position = 0;
        foreach (var patch in patches)
        {
            output.Write(data.AsSpan(position, patch.Offset - position));
            output.Write(patch.Bytes);
            position = patch.End;
        }

        output.Write(data.AsSpan(position));
        return output.ToArray();

        int Relocate(int offset) => offset + patches.Where(p => p.End <= offset).Sum(p => p.Growth);
        int GrowthInside(int offset, int length)
        {
            foreach (var patch in patches.Where(p => p.Offset < offset + length && p.End > offset))
            {
                if (patch.Offset < offset || patch.End > offset + length)
                {
                    throw new SaveFormatException("SAV3 edit crosses a variable boundary.");
                }
            }

            return patches.Where(p => p.Offset >= offset && p.End <= offset + length).Sum(p => p.Growth);
        }

        void Put(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
    }
}
