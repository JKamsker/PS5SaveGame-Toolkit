using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record PackageField(string Name, string Type, int Offset, int Length);

internal sealed class PackageClass
{
    private const int FieldHeaderSize = 8;
    private readonly RedPackage _package;
    private readonly byte[] _bytes;
    private readonly int _start;
    private readonly int _end;

    public PackageClass(RedPackage package, byte[] bytes, int start, int end)
    {
        _package = package;
        _bytes = bytes;
        _start = start;
        _end = end;
        var count = RedPackage.UInt16(bytes, start);
        var headerEnd = start + 2 + count * FieldHeaderSize;
        if (end > bytes.Length || headerEnd > end)
        {
            throw new SaveFormatException("RED class field headers exceed their containing object.");
        }

        var fields = new List<PackageField>();
        for (var index = 0; index < count; index++)
        {
            var header = start + 2 + index * FieldHeaderSize;
            var offset = BinaryData.Int32(bytes, header + 4);
            var next = index + 1 < count ? BinaryData.Int32(bytes, header + FieldHeaderSize + 4) : end - start;
            if (offset < headerEnd - start || next <= offset || next > end - start)
            {
                throw new SaveFormatException("RED class fields are not ordered within their containing object.");
            }

            fields.Add(new PackageField(package.Name(RedPackage.UInt16(bytes, header)),
                package.Name(RedPackage.UInt16(bytes, header + 2)), start + offset, next - offset));
        }

        if (fields.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            throw new SaveFormatException("RED class has duplicate field names.");
        }

        Fields = fields;
    }

    public IReadOnlyList<PackageField> Fields { get; }
    public PackageField? Find(string name) => Fields.SingleOrDefault(f => f.Name == name);
    public PackageField Require(string name, string type) => Find(name) is { } field && field.Type == type ? field
        : throw new SaveFormatException($"RED class is missing verified field {name} ({type}).");
    public byte[] Raw(PackageField field) => BinaryData.Slice(_bytes, field.Offset, field.Length).ToArray();
    public int Int(string name) => Find(name) is { } field ? BinaryData.Int32(Raw(Require(name, "Int32")), 0) : 0;
    public string Enum(string name, string type, string defaultName) => Find(name) is not null
        ? _package.Name(RedPackage.UInt16(Raw(Require(name, type)), 0)) : defaultName;

    // Only these flat structs are traversed. Unknown nested arrays/buffers stay opaque.
    public IReadOnlyList<PackageClass> StructArray(PackageField field)
    {
        var count = RedPackage.Count(_bytes, field.Offset);
        var result = new List<PackageClass>();
        var position = field.Offset + 4;
        for (var index = 0; index < count; index++)
        {
            var probe = new PackageClass(_package, _bytes, position, field.Offset + field.Length);
            var end = position + 2 + probe.Fields.Count * FieldHeaderSize;
            foreach (var member in probe.Fields)
            {
                var size = member.Type switch
                {
                    "Int32" or "Uint32" or "Float" => 4,
                    "Uint64" or "TweakDBID" => 8,
                    "Bool" => 1,
                    "DataBuffer" => 4 + RedPackage.Count(_bytes, member.Offset),
                    "array:gamedataStatType" => 4 + 2 * RedPackage.Count(_bytes, member.Offset),
                    _ when member.Type.StartsWith("gamedata", StringComparison.Ordinal) || member.Type == "gameStatIDType" => 2,
                    _ => throw new SaveFormatException($"Unsupported progression struct member type '{member.Type}'."),
                };
                if (size > member.Length)
                {
                    throw new SaveFormatException("Progression struct members overlap or exceed their object.");
                }

                end = Math.Max(end, checked(member.Offset + size));
            }

            result.Add(new PackageClass(_package, _bytes, position, end));
            position = end;
        }

        if (position != field.Offset + field.Length)
        {
            throw new SaveFormatException("Progression struct array has unexplained trailing bytes.");
        }

        return result;
    }

    public byte[] Rebuild(IReadOnlyDictionary<string, byte[]> replacements)
    {
        if (replacements.Count == 0 || replacements.All(pair => Find(pair.Key) is { } field && Raw(field).AsSpan().SequenceEqual(pair.Value)))
        {
            return _bytes.AsSpan(_start, _end - _start).ToArray();
        }

        var added = replacements.Keys.Where(name => Find(name) is null).ToArray();
        var count = Fields.Count + added.Length;
        var headers = new byte[2 + count * FieldHeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(headers, checked((ushort)count));
        using var data = new MemoryStream();
        // Preserve any padding before the first property body.
        var oldHeaderEnd = _start + 2 + Fields.Count * FieldHeaderSize;
        var firstBody = Fields.Count > 0 ? Fields[0].Offset : _end;
        data.Write(_bytes.AsSpan(oldHeaderEnd, firstBody - oldHeaderEnd));
        var index = 0;
        foreach (var name in Fields.Select(f => f.Name).Concat(added))
        {
            var field = Find(name);
            var header = headers.AsSpan(2 + index++ * FieldHeaderSize);
            BinaryPrimitives.WriteUInt16LittleEndian(header, _package.NameIndex(name));
            BinaryPrimitives.WriteUInt16LittleEndian(header[2..], _package.NameIndex(field?.Type ?? "Int32"));
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], checked(headers.Length + (int)data.Position));
            data.Write(replacements.TryGetValue(name, out var value) ? value : Raw(field!));
        }

        return [.. headers, .. data.ToArray()];
    }
}
