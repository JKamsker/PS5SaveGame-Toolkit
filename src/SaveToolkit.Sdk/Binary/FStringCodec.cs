using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Sdk.Binary;

/// <summary>Signed-length, NUL-terminated UTF-8 or little-endian UTF-16; malformed encoding is rejected.</summary>
public static class FStringCodec
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

    /// <summary>Checks the signed prefix and returns body bytes including the terminator, excluding the four-byte prefix.</summary>
    public static int ByteLength(int signedLength, FStringLimits limits, string context)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        var units = Math.Abs((long)signedLength);
        var bytes = units * (signedLength < 0 ? sizeof(char) : sizeof(byte));
        if (bytes > int.MaxValue || bytes > limits.MaxBytes ||
            (signedLength < 0 ? units > limits.MaxUtf16CodeUnits : bytes > (limits.MaxUtf8Bytes ?? limits.MaxBytes)))
            throw new SaveFormatException($"{context}: FString length exceeds its byte or UTF-16 code-unit limit.");
        return (int)bytes;
    }

    /// <summary>Consumes a signed length prefix and its bounded string body, rejecting malformed encoding or terminators.</summary>
    public static string Read(ref BoundedBinaryCursor cursor, FStringLimits limits)
    {
        var length = cursor.ReadInt32LE();
        var bytes = cursor.Take(ByteLength(length, limits, cursor.Context));
        return Decode(length, bytes, limits, cursor.Context);
    }

    /// <summary>Stream adapter for existing BinaryReader parsers. Does not close or seek the reader.</summary>
    public static string Read(BinaryReader reader, FStringLimits limits, string context)
    {
        ArgumentNullException.ThrowIfNull(reader);
        try
        {
            var length = reader.ReadInt32();
            var size = ByteLength(length, limits, context);
            var stream = reader.BaseStream;
            if (stream.CanSeek && size > stream.Length - stream.Position)
                throw new SaveFormatException($"{context}: truncated FString body.");
            var bytes = new byte[size];
            stream.ReadExactly(bytes);
            return Decode(length, bytes, limits, context);
        }
        catch (EndOfStreamException ex)
        {
            throw new SaveFormatException($"{context}: truncated FString.", ex);
        }
    }

    /// <summary>Requires exactly the prefixed body size. Zero length has no terminator or body.</summary>
    public static string Decode(int signedLength, ReadOnlySpan<byte> bytes, FStringLimits limits, string context)
    {
        var expected = ByteLength(signedLength, limits, context);
        if (bytes.Length != expected)
            throw new SaveFormatException($"{context}: FString body does not match its declared length.");
        if (signedLength == 0) return string.Empty;
        var terminator = signedLength < 0 ? sizeof(char) : sizeof(byte);
        if (bytes[^1] != 0 || terminator == sizeof(char) && bytes[^2] != 0)
            throw new SaveFormatException($"{context}: FString is missing its NUL terminator.");
        try { return (signedLength < 0 ? Utf16 : Utf8).GetString(bytes[..^terminator]); }
        catch (DecoderFallbackException ex) { throw new SaveFormatException($"{context}: invalid FString encoding.", ex); }
    }

    /// <summary>
    /// Writes the explicitly selected encoding, including a terminator even for empty text.
    /// Does not normalize text or choose wide encoding. Retain original bytes for lossless no-op serialization.
    /// </summary>
    public static byte[] Encode(string value, bool wide, FStringLimits limits)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (value.Contains('\0')) throw new ArgumentException("FString text cannot contain NUL.", nameof(value));
        var encoding = wide ? Utf16 : Utf8;
        try
        {
            var bodyLength = (long)encoding.GetByteCount(value) + (wide ? sizeof(char) : sizeof(byte));
            if (bodyLength > int.MaxValue - sizeof(int))
                throw new ArgumentException("FString is too long.", nameof(value));
            var signedLength = wide ? -(int)(bodyLength / sizeof(char)) : (int)bodyLength;
            _ = ByteLength(signedLength, limits, "FString encoding");
            var result = new byte[sizeof(int) + (int)bodyLength];
            BinaryPrimitives.WriteInt32LittleEndian(result, signedLength);
            encoding.GetBytes(value, result.AsSpan(sizeof(int)));
            return result;
        }
        catch (EncoderFallbackException ex) { throw new ArgumentException("Invalid FString text encoding.", nameof(value), ex); }
    }
}
