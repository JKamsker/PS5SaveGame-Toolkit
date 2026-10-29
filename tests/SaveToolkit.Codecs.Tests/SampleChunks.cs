using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace SaveToolkit.Codecs.Tests;

internal static class SampleChunks
{
    private const ulong Magic = 0x9E2A83C1;
    private const int HeaderLength = 48;
    private const int BlockLength = 131072;
    private static readonly ConcurrentDictionary<(string Archive, string Name, string? Slot), Lazy<byte[]>> Cache = new();

    internal static byte[] Read(string titleId, string fileName, string? slot = null)
    {
        string? directory = Environment.GetEnvironmentVariable("SAVETOOLKIT_ARTIFACTS");
        string? archive = directory is not null && Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, $"{titleId}_saves_dec*.zip").FirstOrDefault() : null;
        if (archive is null) Assert.Skip($"Sample {titleId} unavailable; set SAVETOOLKIT_ARTIFACTS.");
        // Cache decompression, not mutable test inputs: every caller still receives its own bytes.
        return Cache.GetOrAdd((archive, fileName, slot), key => new Lazy<byte[]>(() =>
            ReadEntry(key.Archive, key.Name, key.Slot))).Value.ToArray();
    }

    private static byte[] ReadEntry(string archive, string fileName, string? slot)
    {
        using var zip = ZipFile.OpenRead(archive);
        var entry = zip.Entries.FirstOrDefault(e => e.Name == fileName && (slot is null || e.FullName.Contains($"/{slot}/", StringComparison.Ordinal)));
        if (entry is null) Assert.Skip($"Sample entry {fileName} unavailable.");
        using var input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    // These two save families use six little-endian u64s per independently compressed chunk.
    // Keep game/container framing in the tests, outside the shared codec API.
    internal static (byte[] Bytes, int End, int Blocks) DecodeFrames(byte[] file, int start, int expectedBlocks)
    {
        using var decoded = new MemoryStream();
        int offset = start;
        for (int block = 0; block < expectedBlocks; block++)
        {
            var frame = file.AsSpan(offset, HeaderLength);
            Assert.Equal(Magic, BinaryPrimitives.ReadUInt64LittleEndian(frame));
            Assert.Equal(BlockLength, (long)BinaryPrimitives.ReadUInt64LittleEndian(frame[8..]));
            int compressed = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(frame[16..]));
            int plain = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(frame[24..]));
            Assert.Equal(compressed, (long)BinaryPrimitives.ReadUInt64LittleEndian(frame[32..]));
            Assert.Equal(plain, (long)BinaryPrimitives.ReadUInt64LittleEndian(frame[40..]));
            Assert.InRange(plain, 1, BlockLength);
            byte[] chunk = OodleCodec.Decompress(file.AsSpan(offset + HeaderLength, compressed), plain);
            decoded.Write(chunk);
            offset += HeaderLength + compressed;
        }
        return (decoded.ToArray(), offset, expectedBlocks);
    }

    internal static int FindFrames(byte[] file, int offset)
    {
        ReadOnlySpan<byte> magic = [0xC1, 0x83, 0x2A, 0x9E, 0, 0, 0, 0];
        int index = file.AsSpan(offset).IndexOf(magic);
        Assert.True(index >= 0);
        return offset + index;
    }
}
