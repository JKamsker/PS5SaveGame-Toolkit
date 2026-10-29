using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Repairs known lengths and virtual pointers before the writer splices the original image.</summary>
internal sealed class Sav3Relocation(byte[] original, Sav3Directory directory, int headerSize, Sav3Patch[] patches)
{
    private readonly int _table = directory.VariableTableOffset;
    private readonly int _roots = BinaryData.PayloadOffset(original, directory.VariableTableOffset - 6, headerSize);

    public byte[] Apply(Sav3Tree tree)
    {
        var data = original.ToArray();
        UpdateLengths(data, tree);
        UpdateVariables(data);
        UpdateRoots(data);
        foreach (var pointer in new[] { _table - 10, _table - 6, original.Length - 6 })
        {
            Put(data, pointer, Relocate(BinaryData.PayloadOffset(original, pointer, headerSize)) + headerSize);
        }

        return data;
    }

    private void UpdateLengths(byte[] data, Sav3Tree tree)
    {
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
                Put(data, node.Offset + lengthOffset, BinaryData.Int32(original, node.Offset + lengthOffset) + growth);
            }
        }
    }

    private void UpdateVariables(byte[] data)
    {
        // Iterate the stored directory, not the sorted explorer: table order is opaque and preserved.
        for (var index = 0; index < directory.Entries.Count; index++)
        {
            var entry = _table + 4 + index * 8;
            var offset = BinaryData.PayloadOffset(original, entry, headerSize);
            var length = BinaryData.Int32(original, entry + 4);
            Put(data, entry, Relocate(offset) + headerSize);
            Put(data, entry + 4, length + GrowthInside(offset, length));
        }
    }

    private void UpdateRoots(byte[] data)
    {
        var rbCount = BinaryData.Int32(original, _roots + 2);
        for (var index = 0; index < rbCount; index++)
        {
            var entry = _roots + 6 + index * 6 + 2;
            Put(data, entry, Relocate(BinaryData.PayloadOffset(original, entry, headerSize)) + headerSize);
        }
    }

    private int Relocate(int offset) => offset + patches.Where(p => p.End <= offset).Sum(p => p.Growth);
    private int GrowthInside(int offset, int length)
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

    private static void Put(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
}
