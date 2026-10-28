using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>REDengine compact length: six initial bits, seven continuation bits, narrow or UTF-16LE.</summary>
internal static class SavedString
{
    private const int MaximumCharacters = 4096;

    public static string Read(ReadOnlySpan<byte> data, ref int position)
    {
        var bytes = ReadEncoded(data, ref position, out var narrow);
        return Decode(bytes, narrow);
    }

    public static ReadOnlySpan<byte> ReadEncoded(ReadOnlySpan<byte> data, ref int position, out bool narrow)
    {
        var first = BinaryData.Slice(data, position++, 1)[0];
        var length = first & 0x3F;
        var more = (first & 0x40) != 0;
        var shift = 6;
        while (more)
        {
            if (shift > 13) { throw new SaveFormatException("Saved string length exceeds the supported bound."); }
            var next = BinaryData.Slice(data, position++, 1)[0];
            length |= (next & 0x7F) << shift;
            more = (next & 0x80) != 0;
            shift += 7;
        }

        if (length > MaximumCharacters) { throw new SaveFormatException("Saved string is too long."); }
        narrow = (first & 0x80) != 0;
        var byteLength = length * (narrow ? 1 : sizeof(char));
        var bytes = BinaryData.Slice(data, position, byteLength);
        position += byteLength;
        return bytes;
    }

    public static string Decode(ReadOnlySpan<byte> bytes, bool narrow)
    {
        try
        {
            return narrow ? new UTF8Encoding(false, true).GetString(bytes) : new UnicodeEncoding(false, false, true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new SaveFormatException($"Unsupported saved string encoding: {ex.Message}");
        }
    }
}
