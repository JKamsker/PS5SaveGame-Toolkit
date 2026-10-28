using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

internal sealed class GvasCursor(ReadOnlyMemory<byte> data, int start = 0, int? end = null)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
    public ReadOnlyMemory<byte> Data { get; } = data;
    public int Position { get; set; } = start;
    public int End { get; } = end ?? data.Length;
    public int Remaining => End - Position;

    public ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || length > Remaining)
            throw new SaveFormatException($"Truncated GVAS data at 0x{Position:X} (need {length} bytes).");
        var result = Data.Span.Slice(Position, length);
        Position += length;
        return result;
    }

    public byte Byte() => Take(1)[0];
    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public int Count(int maximum = GvasFile.MaxNodes)
    {
        var count = Int32();
        if (count < 0 || count > maximum)
            throw new SaveFormatException($"Invalid GVAS count {count} at 0x{Position - 4:X}.");
        return count;
    }

    public string String()
    {
        var length = Int32();
        return GvasString.Decode(length, Take(GvasString.ByteLength(length)));
    }

    public static byte[] EncodeString(string value, bool wide)
    {
        if (value.Length >= GvasFile.MaxStringLength || value.Contains('\0'))
            throw new ArgumentException("String is too long or contains NUL.", nameof(value));
        wide |= value.Any(c => c > 127);
        var body = (wide ? Utf16 : Utf8).GetBytes(value + '\0');
        var result = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(result, wide ? -body.Length / 2 : body.Length);
        body.CopyTo(result, 4);
        return result;
    }
}
