using System.Buffers.Binary;
using System.IO.Compression;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportChecksumTests
{
    [Theory]
    [InlineData(CompressionLevel.NoCompression, 0)]
    [InlineData(CompressionLevel.Optimal, 0)]
    [InlineData(CompressionLevel.NoCompression, 9)]
    [InlineData(CompressionLevel.Optimal, 9)]
    [InlineData(CompressionLevel.NoCompression, 250_001)]
    [InlineData(CompressionLevel.Optimal, 250_001)]
    public void ValidCrc_PreservesEmptyKnownVectorAndMultiBufferEntries(CompressionLevel compression, int size)
    {
        var bytes = size == 9 ? "123456789"u8.ToArray() : Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
        var archive = Archive(bytes, compression);
        var central = archive.AsSpan().IndexOf("PK\x01\x02"u8);
        if (size == 9) Assert.Equal(0xCBF43926U, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(central + 16)));
        if (size == 0) Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(central + 16)));

        var set = SaveSetImporter.Import([new("dump.zip", () => new MemoryStream(archive))], TestZips.TempDir());

        Assert.Equal(bytes, set.AllDiskFiles.Single().ReadAllBytes());
        Assert.Equal(size, set.AllDiskFiles.Single().Length);
    }

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Optimal)]
    public void InvalidCrc_AfterMultipleBuffers_PreservesDiagnosticAndRollsBack(CompressionLevel compression)
    {
        var archive = Archive(new byte[250_001], compression);
        var central = archive.AsSpan().IndexOf("PK\x01\x02"u8);
        archive[central + 16] ^= 1;
        var root = TestZips.TempDir();

        var error = Assert.Throws<InvalidDataException>(() => SaveSetImporter.Import(
            [new("dump.zip", () => new MemoryStream(archive))], root));

        Assert.Equal("Could not import archive 'dump.zip': Entry 'data.bin': Entry CRC-32 does not match the imported data; the archive may be corrupt or truncated.", error.Message);
        Assert.Empty(Directory.EnumerateFiles(root));
    }

    private static byte[] Archive(byte[] bytes, CompressionLevel compression)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = archive.CreateEntry("data.bin", compression).Open())
            entry.Write(bytes);
        return output.ToArray();
    }
}
