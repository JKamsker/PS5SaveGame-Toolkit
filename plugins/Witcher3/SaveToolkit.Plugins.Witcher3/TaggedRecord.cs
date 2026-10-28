using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record TaggedProperty(string Name, string Type, int Offset, byte[] Bytes)
{
    public ReadOnlySpan<byte> Value => Bytes.AsSpan(8);
}

/// <summary>Observed REDengine saved structures: zero prefix, bounded properties, uint16 zero terminator.</summary>
internal sealed record TaggedRecord(IReadOnlyList<TaggedProperty> Properties)
{
    public static TaggedRecord Read(byte[] data, ref int position, int end, IReadOnlyList<string> names)
    {
        if (position < 0 || end < 0 || end > data.Length || position > end - 3)
        {
            throw new SaveFormatException("Truncated Witcher saved structure within its parent boundary.");
        }
        if (BinaryData.Slice(data, position++, 1)[0] != 0)
        {
            throw new SaveFormatException("Unsupported Witcher saved structure prefix.");
        }

        var properties = new List<TaggedProperty>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (position <= end - 2)
        {
            var index = BinaryData.UInt16(data, position);
            if (index == 0)
            {
                position += 2;
                return new TaggedRecord(properties);
            }

            if (position > end - 8)
            {
                break;
            }

            var name = Name(index);
            var type = Name(BinaryData.UInt16(data, position + 2));
            var size = BinaryData.Int32(data, position + 4);
            if (size < 4 || size > end - position - 4 || !seen.Add(name))
            {
                throw new SaveFormatException("Invalid Witcher saved property length or duplicate name.");
            }

            properties.Add(new TaggedProperty(name, type, position, data.AsSpan(position, size + 4).ToArray()));
            position += size + 4;
        }

        throw new SaveFormatException("Truncated Witcher saved structure.");

        string Name(int index)
        {
            if (index < 1 || index > names.Count)
            {
                throw new SaveFormatException("Invalid Witcher saved property name/type index.");
            }

            return names[index - 1];
        }
    }

    public TaggedProperty? Find(string name) => Properties.SingleOrDefault(p => p.Name == name);

    public int Integer(string name)
    {
        var property = Find(name);
        if (property is null)
        {
            return 0; // REDengine omits default zero fields.
        }

        if (property.Type != "Int32" || property.Value.Length != sizeof(int))
        {
            throw new SaveFormatException($"Witcher '{name}' is not a saved Int32.");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(property.Value);
    }

    public byte[] Write(IReadOnlyDictionary<string, byte[]> replacements)
    {
        using var output = new MemoryStream();
        output.WriteByte(0);
        foreach (var property in Properties)
        {
            output.Write(replacements.TryGetValue(property.Name, out var bytes) ? bytes : property.Bytes);
        }

        foreach (var bytes in replacements.Where(p => Find(p.Key) is null).Select(p => p.Value))
        {
            output.Write(bytes);
        }

        output.Write([0, 0]);
        return output.ToArray();
    }

    public static byte[] Property(int name, int type, ReadOnlySpan<byte> value)
    {
        var result = new byte[8 + value.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(result, checked((ushort)name));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), checked((ushort)type));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), value.Length + 4);
        value.CopyTo(result.AsSpan(8));
        return result;
    }
}
