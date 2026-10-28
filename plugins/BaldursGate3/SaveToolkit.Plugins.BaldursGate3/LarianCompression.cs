using System.IO.Compression;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

internal enum LarianCompressionMethod { None, Zlib, Lz4, Zstd }

internal static class LarianCompression
{
    private const uint Lz4FrameMagic = 0x184D2204;
    private const byte MethodMask = 0x0f;

    internal static LarianCompressionMethod Method(byte flags)
    {
        var method = flags & MethodMask;
        BinaryData.Require(method <= (int)LarianCompressionMethod.Zstd, $"Unsupported BG3 compression method {method}.");
        return (LarianCompressionMethod)method;
    }

    internal static byte[] Decode(ReadOnlySpan<byte> source, int expectedSize, byte flags, bool framed = false)
    {
        BinaryData.Size((ulong)expectedSize);
        var method = Method(flags);
        if (method == LarianCompressionMethod.None)
        {
            BinaryData.Require(source.Length == expectedSize, "Uncompressed BG3 section has an incorrect size.");
            return source.ToArray();
        }

        var output = new byte[expectedSize];
        if (method == LarianCompressionMethod.Lz4 && !framed)
        {
            var length = LZ4Codec.Decode(source, output);
            BinaryData.Require(length == expectedSize, "Invalid LZ4 block or decompressed size mismatch.");
            return output;
        }

        using var input = new MemoryStream(source.ToArray(), false);
        using Stream decoder = method switch
        {
            LarianCompressionMethod.Zlib => new ZLibStream(input, CompressionMode.Decompress),
            LarianCompressionMethod.Lz4 => LZ4Stream.Decode(input),
            LarianCompressionMethod.Zstd => new ZstdSharp.DecompressionStream(input),
            _ => throw new SaveFormatException("Unsupported BG3 compression."),
        };
        decoder.ReadExactly(output);
        BinaryData.Require(decoder.ReadByte() == -1, "BG3 decompressed data exceeds its declared size.");
        return output;
    }

    internal static bool IsLz4Frame(ReadOnlySpan<byte> source) =>
        source.Length >= sizeof(uint) && BinaryData.U32(source, 0) == Lz4FrameMagic;
}
