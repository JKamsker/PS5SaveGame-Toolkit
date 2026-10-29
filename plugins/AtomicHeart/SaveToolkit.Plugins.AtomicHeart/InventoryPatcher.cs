using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Splice explicit scalar payloads; repair only their enclosing serialized byte counts.</summary>
internal static class InventoryPatcher
{
    private sealed record Patch(ByteSpan Span, byte[] Bytes);

    internal static byte[] Apply(RecordIndex index, IReadOnlyDictionary<TaggedProperty, FieldValue> edits)
    {
        if (edits.Count == 0) return index.Plain.ToArray();
        var patches = edits.Select(e => new Patch(e.Key.Payload, Encode(index.Plain, e.Key, e.Value))).ToList();
        var sizes = new Dictionary<int, int>();
        foreach (var edit in patches)
        {
            var delta = edit.Bytes.Length - edit.Span.Length;
            if (delta == 0) continue;
            AddSize(0, delta); // DataBytes array prefix.
            foreach (var record in index.Objects.Where(r => Contains(r.Payload, edit.Span)))
            {
                AddSize(record.Payload.Offset - sizeof(int), delta);
                foreach (var property in Descendants(record.Properties).Where(p => Contains(p.Payload, edit.Span)))
                {
                    if (property.Type == "StructElement") continue;
                    AddSize(SizeOffset(index.Plain, property.Tag.Offset), delta);
                    // Struct-array element descriptor is not a logical property in the inspection tree.
                    if (property.Type == "ArrayProperty" && property.Name == "Slots")
                        AddSize(SizeOffset(index.Plain, property.Payload.Offset + sizeof(int)), delta);
                }
            }
        }
        foreach (var size in sizes)
        {
            var bytes = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, checked(BinaryPrimitives.ReadInt32LittleEndian(index.Plain.AsSpan(size.Key)) + size.Value));
            patches.Add(new(new(size.Key, sizeof(int)), bytes));
        }
        var length = checked(index.Plain.Length + patches.Sum(p => p.Bytes.Length - p.Span.Length));
        if (length > SaveExtensionFile.MaxBytes) throw new SaveFormatException("Edited SlotData exceeds 128 MiB.");
        var output = new byte[length];
        var source = 0;
        var target = 0;
        foreach (var patch in patches.OrderBy(p => p.Span.Offset))
        {
            if (patch.Span.Offset < source) throw new SaveFormatException("Overlapping inventory patches.");
            var retained = patch.Span.Offset - source;
            index.Plain.AsSpan(source, retained).CopyTo(output.AsSpan(target));
            target += retained;
            patch.Bytes.CopyTo(output, target);
            target += patch.Bytes.Length;
            source = patch.Span.End;
        }
        index.Plain.AsSpan(source).CopyTo(output.AsSpan(target));
        return output;

        void AddSize(int offset, int delta) => sizes[offset] = checked(sizes.GetValueOrDefault(offset) + delta);
    }

    private static byte[] Encode(byte[] plain, TaggedProperty property, FieldValue value)
    {
        if (value.Kind == FieldValueKind.Integer)
        {
            var bytes = new byte[property.Payload.Length];
            if (property.Type == "IntProperty") BinaryPrimitives.WriteInt32LittleEndian(bytes, checked((int)value.AsInteger()));
            else if (property.Type == "Int64Property") BinaryPrimitives.WriteInt64LittleEndian(bytes, checked((long)value.AsInteger()));
            else throw new SaveFormatException("Unsupported inventory integer storage.");
            return bytes;
        }
        var text = value.AsString();
        var wide = BinaryPrimitives.ReadInt32LittleEndian(plain.AsSpan(property.Payload.Offset)) < 0;
        var encoded = (wide ? Encoding.Unicode : Encoding.UTF8).GetBytes(text + '\0');
        var result = new byte[sizeof(int) + encoded.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, wide ? -encoded.Length / 2 : encoded.Length);
        encoded.CopyTo(result, sizeof(int));
        return result;
    }

    private static bool Contains(ByteSpan parent, ByteSpan child) => parent.Offset <= child.Offset && parent.End >= child.End;
    private static int SizeOffset(byte[] bytes, int start)
    {
        var reader = SaveExtensionBinary.Cursor(bytes, start);
        _ = reader.String();
        _ = reader.String();
        return reader.Position;
    }
    private static IEnumerable<TaggedProperty> Descendants(IReadOnlyList<TaggedProperty> properties) =>
        properties.SelectMany(p => new[] { p }.Concat(Descendants(p.Children)));
}
