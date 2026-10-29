using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Reads bounded DDL trees without rewriting any stored bytes or metadata.</summary>
internal sealed class DdlReader(Dat1Container container, Dat1Section section, int prefixLength = 0, int? maxDepth = null)
{
    private const uint Magic = 0x0315_0044;
    private const uint CrcPolynomial = 0xEDB8_8320;
    private const int HeaderSize = 16;
    private const int DescriptorSize = 12;
    private const int MaxFields = 1024;
    private const int MaxValues = 524288;
    private const int MaxObjects = 32768;
    private const int MaxDepth = 32;
    private const int MaxTotalFields = 65536;
    private const int MaxTextBytes = 4 * 1024 * 1024;
    private const int MaxStringLength = 65536;
    private readonly UTF8Encoding _utf8 = new(false, true);
    private readonly int _end = checked((int)section.End);
    private int _position = checked((int)section.Offset + prefixLength);
    private int _limit = checked((int)section.End);
    private int _totalValues;
    private int _totalObjects;
    private int _totalFields;
    private int _textBytes;
    private readonly Dictionary<uint, (string Text, uint Hash)> _names = [];

    internal bool DepthLimitExceeded { get; private set; }

    internal IReadOnlyDictionary<string, DdlField> Read()
    {
        var root = ReadObject(0);
        if (_position != _end) throw Invalid("Root object size does not match its section.");
        return root.Fields;
    }

    private DdlObject ReadObject(int depth)
    {
        if (maxDepth is { } budget && depth > budget && budget < MaxDepth)
            // Continue format validation: a host evidence cap cannot make malformed bytes valid.
            DepthLimitExceeded = true;
        if (depth > MaxDepth || ++_totalObjects > MaxObjects)
            throw Invalid("Object depth or node budget exceeded.");
        var start = _position;
        var (count, size) = ReadHeader();
        _totalFields = checked(_totalFields + count);
        if (_totalFields > MaxTotalFields) throw Invalid("Total field budget exceeded.");
        var parentLimit = _limit;
        _limit = checked(_position + size);

        var descriptors = Take(checked(count * DescriptorSize));
        // PS5 nested objects can begin unaligned; their descriptor/value boundary is
        // aligned to the file, while individual scalar fields remain packed.
        if (count > 0 && Take((4 - _position % 4) % 4).ContainsAnyExcept((byte)0))
            throw Invalid("Nonzero padding before object values.");
        var fields = new Dictionary<string, DdlField>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var hash = BinaryPrimitives.ReadUInt32LittleEndian(descriptors[(index * 8)..]);
            var meta = BinaryPrimitives.ReadUInt32LittleEndian(descriptors[(index * 8 + 4)..]);
            var nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(descriptors[(count * 8 + index * 4)..]);
            var name = ReadName(nameOffset);
            if (name.Hash != hash)
                throw Invalid("Field name does not match its identifier hash.");

            var kind = (DdlValueKind)(meta >> 24);
            var length = checked((int)((meta >> 4) & 0xFFFFF));
            _totalValues = checked(_totalValues + length);
            if (!Enum.IsDefined(kind) || _totalValues > MaxValues || (meta & 0xF) > 3)
                throw Invalid("Unsupported field count or array kind.");

            var offset = _position;
            // Counts have passed the aggregate budget. Cap capacity by physical bytes too,
            // so a truncated declaration cannot allocate its claimed array before rejection.
            var remaining = _limit - _position;
            var values = new List<FieldValue>(kind is DdlValueKind.Null or DdlValueKind.Object
                ? 0 : Math.Min(length, remaining / MinimumValueSize(kind)));
            var objects = new List<DdlObject>(kind == DdlValueKind.Object
                ? Math.Min(length, remaining / HeaderSize) : 0);
            if (kind == DdlValueKind.Null)
            {
                // Empty arrays use type 19, count zero, and one stored null byte on PS5.
                if (length != 0 || Take(1)[0] != 0) throw Invalid("Invalid empty field.");
            }
            else
            {
                for (var valueIndex = 0; valueIndex < length; valueIndex++)
                {
                    if (kind == DdlValueKind.Object) objects.Add(ReadObject(depth + 1));
                    else values.Add(ReadValue(kind));
                }
            }

            if (!fields.TryAdd(name.Text, new DdlField(name.Text, kind, length, offset, values) { Objects = objects }))
                throw Invalid("Duplicate field name.");
        }

        if (_limit - _position > 3 || Take(_limit - _position).ContainsAnyExcept((byte)0))
            throw Invalid("Unexpected bytes after the object fields.");
        _limit = parentLimit;
        return new DdlObject(start, HeaderSize + size, fields);
    }

    private static int MinimumValueSize(DdlValueKind kind) => kind switch
    {
        DdlValueKind.UInt16 or DdlValueKind.Int16 => sizeof(ushort),
        DdlValueKind.UInt32 or DdlValueKind.Int32 or DdlValueKind.Float => sizeof(uint),
        DdlValueKind.UInt64 or DdlValueKind.Int64 or DdlValueKind.Identifier or DdlValueKind.Asset or
            DdlValueKind.Double => sizeof(ulong),
        DdlValueKind.String or DdlValueKind.Enum or DdlValueKind.Bitfield or DdlValueKind.File or
            DdlValueKind.Json => HeaderSize + sizeof(uint),
        _ => 1,
    };

    internal static uint NameHash(ReadOnlySpan<byte> name)
    {
        var crc = CrcPolynomial;
        foreach (var value in name)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : CrcPolynomial);
        }
        return crc;
    }

    private (int Count, int Size) ReadHeader()
    {
        var bytes = Take(HeaderSize);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != Magic ||
            count > MaxFields || size > _limit - _position || (ulong)count * DescriptorSize > size)
            throw Invalid("Invalid object header or bounds.");
        return (checked((int)count), checked((int)size));
    }

    private FieldValue ReadValue(DdlValueKind kind) => kind switch
    {
        DdlValueKind.UInt8 => FieldValue.Of((long)Take(1)[0]),
        DdlValueKind.UInt16 => FieldValue.Of((long)BinaryPrimitives.ReadUInt16LittleEndian(Take(2))),
        DdlValueKind.UInt32 => FieldValue.Of((long)BinaryPrimitives.ReadUInt32LittleEndian(Take(4))),
        DdlValueKind.UInt64 or DdlValueKind.Identifier or DdlValueKind.Asset =>
            FieldValue.Of(BinaryPrimitives.ReadUInt64LittleEndian(Take(8))),
        DdlValueKind.Int8 => FieldValue.Of((long)unchecked((sbyte)Take(1)[0])),
        DdlValueKind.Int16 => FieldValue.Of((long)BinaryPrimitives.ReadInt16LittleEndian(Take(2))),
        DdlValueKind.Int32 => FieldValue.Of((long)BinaryPrimitives.ReadInt32LittleEndian(Take(4))),
        DdlValueKind.Int64 => FieldValue.Of(BinaryPrimitives.ReadInt64LittleEndian(Take(8))),
        DdlValueKind.Float => Finite(BinaryPrimitives.ReadSingleLittleEndian(Take(4))),
        DdlValueKind.Double => Finite(BinaryPrimitives.ReadDoubleLittleEndian(Take(8))),
        DdlValueKind.Bool => ReadBool(),
        DdlValueKind.String or DdlValueKind.Enum or DdlValueKind.Bitfield or DdlValueKind.File or DdlValueKind.Json =>
            FieldValue.Of(ReadString()),
        _ => throw Invalid($"Unsupported value type {(byte)kind}."),
    };

    private FieldValue ReadBool() => Take(1)[0] switch
    {
        0 => FieldValue.Of(false),
        1 => FieldValue.Of(true),
        _ => throw Invalid("Boolean is neither zero nor one."),
    };

    private FieldValue Finite(double value) => double.IsFinite(value)
        ? FieldValue.Of(value) : throw Invalid("Non-finite number.");

    private string ReadString()
    {
        // The 32/64-bit words describe the string, not a checksum of this section.
        var length = BinaryPrimitives.ReadUInt32LittleEndian(Take(HeaderSize));
        if (length > MaxStringLength) throw Invalid("String is too long.");
        CountText(checked((int)length));
        var bytes = Take(checked((int)length));
        if (Take(1)[0] != 0) throw Invalid("String terminator is missing.");
        // Padding is relative to this string's payload, even at unaligned addresses.
        _ = Take((4 - (checked((int)length) + 1) % 4) % 4);
        return Decode(bytes);
    }

    private (string Text, uint Hash) ReadName(uint offset)
    {
        if (_names.TryGetValue(offset, out var name)) return name;
        if (offset < container.DescriptorEnd || offset >= container.DataStart)
            throw Invalid("Field name points outside the string table.");
        var bytes = container.Bytes.AsSpan(checked((int)offset), container.DataStart - checked((int)offset));
        var length = bytes.IndexOf((byte)0);
        if (length is < 1 or > MaxStringLength) throw Invalid("Invalid field name.");
        CountText(length);
        name = (Decode(bytes[..length]), NameHash(bytes[..length]));
        _names.Add(offset, name);
        return name;
    }

    private void CountText(int length)
    {
        _textBytes = checked(_textBytes + length);
        if (_textBytes > MaxTextBytes) throw Invalid("Text budget exceeded.");
    }

    private string Decode(ReadOnlySpan<byte> bytes)
    {
        try { return _utf8.GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new SaveFormatException("Invalid DDL UTF-8.", exception); }
    }

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || length > _limit - _position) throw Invalid("Field exceeds the object bounds.");
        var bytes = container.Bytes.AsSpan(_position, length);
        _position += length;
        return bytes;
    }

    private SaveFormatException Invalid(string message) => new($"DDL section 0x{section.Tag:X8}: {message}");
}
