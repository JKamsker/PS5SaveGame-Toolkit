using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

internal static class GvasString
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

    public static int ByteLength(int length)
    {
        if (length == int.MinValue || Math.Abs(length) > GvasFile.MaxStringLength)
            throw new SaveFormatException("GVAS string exceeds the supported length limit.");
        return Math.Abs(length) * (length < 0 ? 2 : 1);
    }

    public static string Decode(int length, ReadOnlySpan<byte> bytes)
    {
        if (length == 0) return string.Empty;
        var wide = length < 0;
        var terminator = wide ? 2 : 1;
        if (bytes[^1] != 0 || (wide && bytes[^2] != 0))
            throw new SaveFormatException("GVAS string is missing its NUL terminator.");
        try { return (wide ? Utf16 : Utf8).GetString(bytes[..^terminator]); }
        catch (DecoderFallbackException ex) { throw new SaveFormatException("Invalid GVAS string encoding.", ex); }
    }

    public static string Read(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var byteLength = ByteLength(length);
        var stream = reader.BaseStream;
        if (stream.CanSeek && byteLength > stream.Length - stream.Position)
            throw new SaveFormatException("Truncated GVAS string.");
        var bytes = new byte[byteLength];
        stream.ReadExactly(bytes);
        return Decode(length, bytes);
    }
}
