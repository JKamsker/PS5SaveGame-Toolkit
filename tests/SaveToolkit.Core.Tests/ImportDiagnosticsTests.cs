using System.Buffers.Binary;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportDiagnosticsTests
{
    [Fact]
    public void ZIP_checksum_matches_standard_IEEE_vector_across_chunks()
    {
        var checksum = new ZipEntryChecksum();
        Assert.Equal(0U, checksum.Value);
        checksum.Append("1234"u8);
        checksum.Append("56789"u8);
        Assert.Equal(0xCBF43926U, checksum.Value);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Rejects_entry_lengths_that_disagree_with_decompressed_bytes(int declaredLength)
    {
        byte[] archive = TestZips.Build(("PPSA00001/sdimg_SAVE/data.bin", [1, 2, 3]));
        int centralDirectory = archive.AsSpan().IndexOf("PK\x01\x02"u8);
        Assert.True(centralDirectory >= 0);
        BinaryPrimitives.WriteInt32LittleEndian(archive.AsSpan(centralDirectory + 24), declaredLength);

        var error = Assert.Throws<InvalidDataException>(() => SaveSetImporter.Import(
            [new ImportSource("dump.zip", () => new MemoryStream(archive))], TestZips.TempDir()));

        Assert.Contains("dump.zip", error.Message, StringComparison.Ordinal);
        Assert.Contains("data.bin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_archive_keeps_source_name_and_original_exception()
    {
        var error = Assert.Throws<InvalidDataException>(() => SaveSetImporter.Import(
            [new ImportSource("broken.zip", () => new MemoryStream([1, 2, 3]))], TestZips.TempDir()));

        Assert.Contains("broken.zip", error.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidDataException>(error.InnerException);
    }

    [Fact]
    public void Rejects_entry_with_incorrect_CRC_even_when_length_matches()
    {
        byte[] archive = TestZips.Build(("data.bin", [1, 2, 3]));
        int centralDirectory = archive.AsSpan().IndexOf("PK\x01\x02"u8);
        archive[centralDirectory + 16] ^= 1;

        var error = Assert.Throws<InvalidDataException>(() => SaveSetImporter.Import(
            [new ImportSource("dump.zip", () => new MemoryStream(archive))], TestZips.TempDir()));

        Assert.Contains("CRC", error.Message, StringComparison.Ordinal);
    }
}
