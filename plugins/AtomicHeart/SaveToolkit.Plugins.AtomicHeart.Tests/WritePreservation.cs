using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

internal static class WritePreservation
{
    internal static void AssertReferenceDiff(RecordIndex before, RecordIndex after, InventoryItem item, InventoryItem replacement)
    {
        // Align the variable-length scalar back to the original. Every remaining difference
        // must be one of the explicitly enclosing length fields, never an unknown/native byte.
        var aligned = new byte[before.Plain.Length];
        after.Plain.AsSpan(0, replacement.Asset.Payload.Offset).CopyTo(aligned);
        before.Plain.AsSpan(item.Asset.Payload.Offset, item.Asset.Payload.Length).CopyTo(aligned.AsSpan(item.Asset.Payload.Offset));
        after.Plain.AsSpan(replacement.Asset.Payload.End).CopyTo(aligned.AsSpan(item.Asset.Payload.End));
        var allowed = new HashSet<int> { 0 };
        foreach (var record in before.Objects.Where(r => Contains(r.Payload, item.Asset.Payload)))
        {
            allowed.Add(record.Payload.Offset - sizeof(int));
            foreach (var property in All(record.Properties).Where(p => Contains(p.Payload, item.Asset.Payload) && p.Type != "StructElement"))
            {
                allowed.Add(SizePosition(before.Plain, property.Tag.Offset));
                if (property.Type == "ArrayProperty" && property.Name == "Slots")
                    allowed.Add(SizePosition(before.Plain, property.Payload.Offset + sizeof(int)));
            }
        }
        var diff = ByteDiffer.Compare(before.Plain, aligned, options: new() { MergeGap = 0 });
        Assert.False(diff.Truncated);
        foreach (var range in diff.Ranges)
            for (var position = range.Offset; position < range.End; position++)
                Assert.Contains(allowed, start => position >= start && position < start + sizeof(int));
    }

    private static bool Contains(ByteSpan parent, ByteSpan child) => parent.Offset <= child.Offset && parent.End >= child.End;
    private static IEnumerable<TaggedProperty> All(IReadOnlyList<TaggedProperty> properties) =>
        properties.SelectMany(p => new[] { p }.Concat(All(p.Children)));
    private static int SizePosition(byte[] plain, int position)
    {
        var reader = new SaveCursor(plain, position);
        _ = reader.String();
        _ = reader.String();
        return reader.Position;
    }
}
