using System.IO.Compression;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Compression;

namespace SaveToolkit.Plugins.BaldursGate3;

internal enum LarianCompressionMethod { None, Zlib, Lz4, Zstd }

internal static class LarianCompression
{
    private const uint Lz4FrameMagic = 0x184D2204;
    private const byte MethodMask = 0x0f;
    private const byte LevelMask = 0xf0;
    private const byte FastLevel = 0x10;
    private const byte MaximumLevel = 0x40;
    private const int ZstdFastLevel = 3;
    private const int ZstdDefaultLevel = 9;
    private const int ZstdMaximumLevel = 22;

    internal static LarianCompressionMethod Method(byte flags)
    {
        var method = flags & MethodMask;
        BinaryData.Require(method <= (int)LarianCompressionMethod.Zstd, $"Unsupported BG3 compression method {method}.");
        return (LarianCompressionMethod)method;
    }

    internal static byte[] Decode(ReadOnlySpan<byte> source, int expectedSize, byte flags, bool framed = false)
    {
        try { return DecodeCore(source, expectedSize, flags, framed); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ZstdSharp.ZstdException or ArgumentException)
        {
            throw new SaveFormatException($"Invalid BG3 compressed data: {exception.Message}", exception);
        }
    }

    private static byte[] DecodeCore(ReadOnlySpan<byte> source, int expectedSize, byte flags, bool framed)
    {
        BinaryData.Size((ulong)expectedSize);
        var method = Method(flags);
        if (method == LarianCompressionMethod.None)
        {
            BinaryData.Require(source.Length == expectedSize, "Uncompressed BG3 section has an incorrect size.");
            return source.ToArray();
        }

        BinaryData.Require(!source.IsEmpty, "BG3 compressed member is empty or truncated.");

        if (method == LarianCompressionMethod.Lz4 && !framed)
        {
            Lz4BlockValidation.Validate(source, expectedSize);
            if (expectedSize == 0) return []; // K4os requires a nonempty destination, even for the valid zero-literal token.
            var output = new byte[expectedSize];
            var length = LZ4Codec.Decode(source, output);
            BinaryData.Require(length == expectedSize, "Invalid LZ4 block or decompressed size mismatch.");
            return output;
        }

        return CompressedStreamValidation.Decode(source, expectedSize, method);
    }

    internal static bool IsLz4Frame(ReadOnlySpan<byte> source) =>
        source.Length >= sizeof(uint) && BinaryData.U32(source, 0) == Lz4FrameMagic;

    internal static byte[] Encode(byte[] source, byte flags, bool framed = false)
    {
        var method = Method(flags);
        var level = flags & LevelMask;
        var lz4Level = level switch { FastLevel => LZ4Level.L00_FAST, MaximumLevel => LZ4Level.L12_MAX, _ => LZ4Level.L10_OPT };
        if (method == LarianCompressionMethod.None)
        {
            return source.ToArray();
        }

        if (method == LarianCompressionMethod.Lz4 && !framed)
        {
            var buffer = new byte[LZ4Codec.MaximumOutputSize(source.Length)];
            // The SDK ports write native K4os' bytes everywhere: K4os' HC encoder traps the browser
            // runtime, and its fast level there is the 32-bit engine (different bytes over 64 KiB).
            var length = lz4Level == LZ4Level.L00_FAST
                ? Lz4FastCompression.Encode(source, buffer)
                : Lz4HighCompression.Encode(source, buffer, (int)lz4Level);
            BinaryData.Require(length > 0, "Unable to compress an LZ4 block.");
            return buffer[..length];
        }

        using var output = new MemoryStream();
        using (Stream encoder = method switch
        {
            LarianCompressionMethod.Zlib => new ZLibStream(output, level switch
            {
                FastLevel => CompressionLevel.Fastest, MaximumLevel => CompressionLevel.SmallestSize, _ => CompressionLevel.Optimal,
            }, true),
            LarianCompressionMethod.Lz4 => Lz4FrameEncoder(output, lz4Level),
            LarianCompressionMethod.Zstd => new ZstdSharp.CompressionStream(output, level switch
            {
                FastLevel => ZstdFastLevel, MaximumLevel => ZstdMaximumLevel, _ => ZstdDefaultLevel,
            }, leaveOpen: true),
            _ => throw new SaveFormatException("Unsupported BG3 compression."),
        })
        {
            encoder.Write(source);
        }

        return output.ToArray();
    }

    /// <summary>K4os' default LZ4 frame with the managed chain engine swapped in (identical frames).</summary>
    private static Stream Lz4FrameEncoder(Stream output, LZ4Level level)
    {
        var settings = new LZ4EncoderSettings { CompressionLevel = level };
        return new LZ4EncoderStream(output, settings.CreateDescriptor(), descriptor => descriptor.Chaining
            ? new ManagedLz4ChainEncoder(Lz4Chain(level), descriptor.BlockSize, extraBlocks: 0)
            : throw new InvalidOperationException("BG3 LZ4 frames are always block-linked."), leaveOpen: true);
    }

    private static ILz4CompressionChain Lz4Chain(LZ4Level level) => level < LZ4Level.L03_HC
        ? new Lz4FastCompressionChain()
        : new Lz4HighCompressionChain((int)level);
}
