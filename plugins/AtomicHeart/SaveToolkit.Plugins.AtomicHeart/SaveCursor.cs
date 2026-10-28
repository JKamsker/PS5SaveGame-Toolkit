using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed class SaveCursor(ReadOnlyMemory<byte> data, int start = 0, int? end = null)
{
    private const int MaxStringUnits = 1_048_576;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
    internal int Position { get; set; } = start;
    internal int End { get; } = end ?? data.Length;
    internal int Remaining => End - Position;

    internal ReadOnlySpan<byte> Take(int length)
    {
        if (length < 0 || length > Remaining) throw new SaveFormatException($"Truncated SaveExtension data at 0x{Position:X}.");
        var result = data.Span.Slice(Position, length);
        Position += length;
        return result;
    }

    internal byte Byte() => Take(1)[0];
    internal ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    internal long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
    internal ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    internal int Count(int maximum)
    {
        var count = Int32();
        if (count < 0 || count > maximum) throw new SaveFormatException("Invalid SaveExtension count or length.");
        return count;
    }

    internal string String()
    {
        var length = Int32();
        if (length == 0) return string.Empty;
        if (length == int.MinValue || Math.Abs(length) > MaxStringUnits) throw new SaveFormatException("SaveExtension string is too long.");
        var wide = length < 0;
        var bytes = Take(Math.Abs(length) * (wide ? 2 : 1));
        var terminator = wide ? 2 : 1;
        if (bytes[^1] != 0 || (wide && bytes[^2] != 0)) throw new SaveFormatException("Unterminated SaveExtension string.");
        try { return (wide ? Utf16 : Utf8).GetString(bytes[..^terminator]); }
        catch (DecoderFallbackException ex) { throw new SaveFormatException("Invalid SaveExtension string encoding.", ex); }
    }
}
