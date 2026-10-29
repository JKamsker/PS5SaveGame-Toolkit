using SaveToolkit.Codecs.Oodle;

namespace SaveToolkit.Codecs;

/// <summary>Managed Kraken streams, including stored blocks. Container framing belongs to the plugin.</summary>
public static class OodleCodec
{
    public const int DefaultMaxBytes = 64 * 1024 * 1024;
    public const int MaximumBytes = 256 * 1024 * 1024;
    private const int QuantumBytes = 256 * 1024;
    private const int PaddingBytes = 64;
    private const long MaximumOperations = 256_000_000;

    /// <summary>Decode exactly the declared output length and consume the entire input, or throw.</summary>
    /// <remarks>Kraken (decoder 6) only. Checksummed streams and newer excess modes are rejected.</remarks>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed, int outputLength, int maxBytes = DefaultMaxBytes)
    {
        ValidateLimit(outputLength, maxBytes);
        if (compressed.Length > maxBytes + 2 * ((maxBytes + QuantumBytes - 1) / QuantumBytes))
            throw new InvalidDataException("The Kraken input exceeds the configured size limit.");
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("The managed Kraken tables require little-endian layout.");
        if (outputLength == 0)
        {
            if (!compressed.IsEmpty) throw new InvalidDataException("Unexpected Kraken data for empty output.");
            return [];
        }

        var budget = new DecodeBudget(Math.Min(MaximumOperations, checked(1_000_000L + 64L * (compressed.Length + outputLength))));
        var source = new byte[checked(compressed.Length + 2 * PaddingBytes)];
        compressed.CopyTo(source.AsSpan(PaddingBytes));
        var destination = new byte[checked(outputLength + PaddingBytes)];
        try
        {
            int decoded = StreamDecoder.DecompressCore(new(source, PaddingBytes, budget), compressed.Length,
                new(destination, 0, budget), outputLength);
            if (decoded != outputLength)
                throw new InvalidDataException("Invalid or unsupported Kraken stream.");
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("Kraken stream references data outside its bounded buffers.", exception);
        }
        return destination.AsSpan(0, outputLength).ToArray();
    }

    /// <summary>Emit independent stored Kraken blocks. PS5 game reload acceptance requires console validation.</summary>
    public static byte[] EncodeStored(ReadOnlySpan<byte> input, int maxBytes = DefaultMaxBytes)
    {
        ValidateLimit(input.Length, maxBytes);
        int blocks = (input.Length + QuantumBytes - 1) / QuantumBytes;
        var result = new byte[checked(input.Length + blocks * 2)];
        int source = 0, target = 0;
        while (source < input.Length)
        {
            int count = Math.Min(QuantumBytes, input.Length - source);
            result[target++] = 0xCC; // Restart + uncompressed + Oodle block signature.
            result[target++] = 6; // Kraken decoder.
            input.Slice(source, count).CopyTo(result.AsSpan(target));
            source += count;
            target += count;
        }
        return result;
    }

    private static void ValidateLimit(int length, int maxBytes)
    {
        if (maxBytes is < 1 or > MaximumBytes)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (length < 0 || length > maxBytes)
            throw new InvalidDataException("The Kraken output exceeds the configured size limit.");
    }
}
