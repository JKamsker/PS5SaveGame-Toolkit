using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

internal ref struct GvasReader(ReadOnlySpan<byte> data)
{
    private const int MaximumStringBytes = 16_384;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
    private readonly ReadOnlySpan<byte> _data = data;
    public int Position { get; private set; }

    public ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > _data.Length - Position) throw new SaveFormatException("Truncated Gearbox GVAS header.");
        var result = _data.Slice(Position, count);
        Position += count;
        return result;
    }

    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)));
    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));

    public string String()
    {
        var length = Int32();
        if (length == 0) return string.Empty;
        if (length is int.MinValue or > MaximumStringBytes or < -MaximumStringBytes / 2)
            throw new SaveFormatException("Invalid GVAS string length.");
        var unicode = length < 0;
        var terminator = unicode ? 2 : 1;
        var bytes = Take(Math.Abs(length) * terminator);
        if (bytes[^1] != 0 || (unicode && bytes[^2] != 0)) throw new SaveFormatException("GVAS string is not terminated.");
        try { return (unicode ? Utf16 : Utf8).GetString(bytes[..^terminator]); }
        catch (DecoderFallbackException ex) { throw new SaveFormatException("Invalid GVAS string encoding.", ex); }
    }
}
