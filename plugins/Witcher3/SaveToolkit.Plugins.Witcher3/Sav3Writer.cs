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
        var relocation = new Sav3Relocation(original, directory, container.HeaderSize, patches);
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

        var data = relocation.Apply(tree);

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
    }
}
