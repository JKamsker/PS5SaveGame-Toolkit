using System.Buffers.Binary;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

/// <summary>Builds one bounded removal patch for a fully parsed primitive container.</summary>
internal static class GvasStructuralEditor
{
    public static byte[] RemoveLast(GvasFile file, GvasNode node)
    {
        var info = node.Container ?? throw new InvalidOperationException("The selected property has no editable container schema.");
        if (node.Children.Count == 0 || info.ElementType is "StructProperty" or "MapProperty" && info.ValueType == "StructProperty")
            throw new InvalidOperationException("Only non-empty primitive containers can remove an element.");
        if (node.Children.Any(child => child.Scalar is null))
            throw new InvalidOperationException("The container contains an opaque element and remains read-only.");

        int pairCount = info.Kind == "MapProperty" ? 2 : 1;
        if (node.Children.Count < pairCount) throw new InvalidOperationException("The container has no complete element to remove.");
        int first = node.Children[^pairCount].Offset - node.Offset;
        int length = node.Children.Skip(node.Children.Count - pairCount).Sum(child => child.Length);
        if (first < info.PrefixBytes || length <= 0 || first + length > node.Length)
            throw new InvalidOperationException("The container element span is outside its payload.");

        byte[] payload = file.Original.Slice(node.Offset, node.Length).ToArray();
        Buffer.BlockCopy(payload, first + length, payload, first, payload.Length - first - length);
        Array.Resize(ref payload, payload.Length - length);
        int countOffset = info.Kind == "ArrayProperty" ? 0 : sizeof(int);
        int count = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(countOffset));
        if (count <= 0) throw new InvalidOperationException("The container count is invalid.");
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(countOffset), count - 1);
        return payload;
    }
}
