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
        ValidateInput(compressed, outputLength, maxBytes);
        var result = new byte[outputLength];
        using var workspace = new OodleWorkspace();
        Decompress(compressed, result, workspace, maxBytes);
        return result;
    }

    /// <summary>Decode into the caller's exact-length destination using reusable padded workspace buffers.</summary>
    /// <remarks>
    /// The destination length declares the output size. It is copied only after complete validation,
    /// and may overlap the compressed input. The workspace must not be used concurrently.
    /// Limits, supported modes and consumption requirements match the allocating overload.
    /// </remarks>
    public static void Decompress(ReadOnlySpan<byte> compressed, Span<byte> destination,
        OodleWorkspace workspace, int maxBytes = DefaultMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateInput(compressed, destination.Length, maxBytes);
        workspace.Acquire();
        try
        {
            if (destination.IsEmpty) return;
            Decode(compressed, destination, workspace);
        }
        finally
        {
            workspace.Release();
        }
    }

    private static void ValidateInput(ReadOnlySpan<byte> compressed, int outputLength, int maxBytes)
    {
        ValidateLimit(outputLength, maxBytes);
        if (compressed.Length > maxBytes + 2 * ((maxBytes + QuantumBytes - 1) / QuantumBytes))
            throw new InvalidDataException("The Kraken input exceeds the configured size limit.");
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("The managed Kraken tables require little-endian layout.");
        if (outputLength == 0)
        {
            if (!compressed.IsEmpty) throw new InvalidDataException("Unexpected Kraken data for empty output.");
        }
    }

    private static void Decode(ReadOnlySpan<byte> compressed, Span<byte> destination, OodleWorkspace workspace)
    {
        int outputLength = destination.Length;
        var budget = new DecodeBudget(Math.Min(MaximumOperations, checked(1_000_000L + 64L * (compressed.Length + outputLength))));
        int sourceLength = checked(compressed.Length + 2 * PaddingBytes);
        int destinationLength = checked(outputLength + PaddingBytes);
        byte[] source = workspace.Source(sourceLength);
        compressed.CopyTo(source.AsSpan(PaddingBytes));
        byte[] decodedBytes = workspace.Destination(destinationLength);
        try
        {
            int decoded = StreamDecoder.DecompressCore(new(source, PaddingBytes, budget, sourceLength), compressed.Length,
                new(decodedBytes, 0, budget, destinationLength), outputLength, workspace);
            if (decoded != outputLength)
                throw new InvalidDataException("Invalid or unsupported Kraken stream.");
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("Kraken stream references data outside its bounded buffers.", exception);
        }
        decodedBytes.AsSpan(0, outputLength).CopyTo(destination);
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
