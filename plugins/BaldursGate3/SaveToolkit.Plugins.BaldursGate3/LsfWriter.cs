using System.Buffers.Binary;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Patches attribute/value sections; retains names, links, keys, gaps and other bytes.</summary>
internal static class LsfWriter
{
    internal static byte[] Write(byte[] original, IReadOnlyDictionary<int, byte[]> replacements)
    {
        var resource = LsfReader.Read(original);
        foreach (var (index, data) in replacements)
        {
            BinaryData.Require(index >= 0 && index < resource.Attributes.Count, "Unknown LSOF attribute edit.");
            BinaryData.Require(data.Length <= LsfLayout.MaxAttributeLength, "LSOF attribute exceeds its length field.");
            AttributeDecoder.Validate(resource.Attributes[index] with { Data = data });
        }

        if (replacements.All(pair => pair.Value.AsSpan().SequenceEqual(resource.Attributes[pair.Key].Data.Span)))
        {
            return original.ToArray();
        }

        var binary = LsfBinary.Read(original);
        var attributes = binary.Data(LsfLayout.AttributesPair).ToArray();
        var oldValues = binary.Data(LsfLayout.ValuesPair);
        var extended = binary.Metadata == 1;
        var recordSize = extended ? LsfLayout.AdjacencyRecordSize : LsfLayout.CompactRecordSize;
        var ranges = new List<ValueRange>(resource.Attributes.Count);
        var position = 0;
        for (var i = 0; i < resource.Attributes.Count; i++)
        {
            var row = attributes.AsSpan(i * recordSize, recordSize);
            if (extended)
            {
                position = BinaryData.Size(BinaryData.U32(row, LsfLayout.ExtendedValueOffset));
            }

            ranges.Add(new ValueRange(i, position, resource.Attributes[i].Data.Length));
            position += resource.Attributes[i].Data.Length;
        }

        var newLength = (long)oldValues.Length + replacements.Sum(pair => (long)pair.Value.Length - resource.Attributes[pair.Key].Data.Length);
        BinaryData.Require(newLength is >= 0 and <= BinaryData.MaxBufferSize, "Edited LSOF value section exceeds 256 MiB.");
        using var values = new MemoryStream((int)newLength);
        position = 0;
        foreach (var range in ranges.OrderBy(r => r.Offset).ThenBy(r => r.Index))
        {
            BinaryData.Require(range.Offset >= position, "Overlapping LSOF values cannot be edited safely.");
            values.Write(oldValues.AsSpan(position, range.Offset - position));
            var row = attributes.AsSpan(range.Index * recordSize, recordSize);
            if (extended)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(row[LsfLayout.ExtendedValueOffset..], checked((uint)values.Position));
            }

            var data = replacements.TryGetValue(range.Index, out var replacement) ? replacement.AsSpan() : resource.Attributes[range.Index].Data.Span;
            var typeAndLength = ((uint)data.Length << LsfLayout.AttributeTypeBits) | (uint)resource.Attributes[range.Index].Type;
            BinaryPrimitives.WriteUInt32LittleEndian(row[LsfLayout.AttributeTypeAndLength..], typeAndLength);
            values.Write(data);
            position = range.Offset + range.Length;
        }

        values.Write(oldValues.AsSpan(position));
        var result = binary.Rebuild(new Dictionary<int, byte[]>
        {
            [binary.Pair(LsfLayout.AttributesPair)] = attributes,
            [binary.Pair(LsfLayout.ValuesPair)] = values.ToArray(),
        });
        var reopened = LsfReader.Read(result);
        BinaryData.Require(reopened.Attributes.Count == resource.Attributes.Count, "LSOF writer changed the attribute count.");
        for (var i = 0; i < resource.Attributes.Count; i++)
        {
            var expected = replacements.TryGetValue(i, out var data) ? data.AsSpan() : resource.Attributes[i].Data.Span;
            BinaryData.Require(reopened.Attributes[i].Data.Span.SequenceEqual(expected), "LSOF edit did not survive writer validation.");
        }

        return result;
    }

    private sealed record ValueRange(int Index, int Offset, int Length);
}
