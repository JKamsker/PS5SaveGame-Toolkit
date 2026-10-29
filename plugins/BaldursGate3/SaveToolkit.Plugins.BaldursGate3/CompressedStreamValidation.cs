using System.IO.Compression;
using K4os.Compression.LZ4.Streams;

namespace SaveToolkit.Plugins.BaldursGate3;

internal static class CompressedStreamValidation
{
    private const int ScratchBytes = 16 * 1024;
    private const int MaximumWindowLog = 28; // Same 256 MiB ceiling as the document's expanded data.

    internal static byte[] Decode(ReadOnlySpan<byte> source, int expectedSize, LarianCompressionMethod method)
    {
        using var input = method == LarianCompressionMethod.Zlib
            ? new ZlibInputStream(source.ToArray()) : new MemoryStream(source.ToArray(), false);
        // Validate the complete stream with fixed scratch before trusting its declared output size.
        // Reuse the compressed bytes; only the second pass allocates the retained payload buffer.
        using (var decoder = CreateDecoder(input, method))
        {
            Span<byte> scratch = stackalloc byte[ScratchBytes];
            var expanded = 0;
            int count;
            while ((count = decoder.Read(scratch[..Math.Min(scratch.Length, expectedSize - expanded + 1)])) != 0)
            {
                BinaryData.Require(count <= expectedSize - expanded, "BG3 decompressed data exceeds its declared size.");
                expanded += count;
            }
            BinaryData.Require(expanded == expectedSize, "BG3 decompressed size mismatch.");
        }

        input.Position = 0;
        using var secondDecoder = CreateDecoder(input, method);
        var output = new byte[expectedSize];
        secondDecoder.ReadExactly(output);
        BinaryData.Require(secondDecoder.ReadByte() == -1, "BG3 decompressed data exceeds its declared size.");
        return output;
    }

    private static Stream CreateDecoder(Stream input, LarianCompressionMethod method)
    {
        if (method == LarianCompressionMethod.Zstd)
        {
            var decoder = new ZstdSharp.DecompressionStream(input, leaveOpen: true);
            decoder.SetParameter(ZstdSharp.Unsafe.ZSTD_dParameter.ZSTD_d_windowLogMax, MaximumWindowLog);
            return decoder;
        }

        return method == LarianCompressionMethod.Lz4
            ? LZ4Stream.Decode(input, leaveOpen: true)
            : new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true);
    }
}
