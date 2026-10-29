using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportResourceTests
{
    private const int MetadataLimit = 1024 * 1024;

    [Theory]
    [InlineData(MetadataLimit, true)]
    [InlineData(MetadataLimit + 1, false)]
    public void Optional_metadata_parsing_is_bounded_and_preserves_the_complete_file(int size, bool parsed)
    {
        byte[] metadata = new byte[size];
        "\0PSF"u8.CopyTo(metadata);
        var archive = TestZips.Build(("PPSA00001/sdimg_SAVE/sce_sys/param.sfo", metadata));

        var set = SaveSetImporter.Import([new ImportSource("metadata.zip", () => new MemoryStream(archive))], TestZips.TempDir());

        var directory = Assert.IsType<DiskSaveDirectory>(Assert.Single(set.Directories));
        Assert.Equal(parsed, directory.ParamSfo is not null);
        Assert.Equal(metadata, Assert.Single(directory.SystemFiles).ReadAllBytes());
    }

    [Fact]
    public void Declared_entry_larger_than_remaining_budget_is_rejected_before_copying()
    {
        var archive = TestZips.Build(("data.bin", [1]));
        int central = archive.AsSpan().IndexOf("PK\x01\x02"u8);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(central + 24), uint.MaxValue - 1);

        var error = Assert.Throws<ImportException>(() => SaveSetImporter.Import(
            [new ImportSource("huge.zip", () => new MemoryStream(archive))], TestZips.TempDir(),
            new ImportLimits { MaxTotalBytes = MetadataLimit }));

        Assert.Contains("exceeds", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Large_save_respects_the_aggregate_byte_limit(int excess)
    {
        const int largeSize = 8 * 1024 * 1024;
        var bytes = new byte[largeSize];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)i;
        byte[] archive = TestZips.Build(("PPSA00001/sdimg_SAVE/data.bin", bytes), ("extra.bin", [9]));
        string root = TestZips.TempDir();
        DiskSaveSet Import() => SaveSetImporter.Import(
            [new ImportSource("large.zip", () => new MemoryStream(archive))], root,
            new ImportLimits { MaxTotalBytes = largeSize + 1 - excess });

        if (excess > 0)
        {
            Assert.Throws<ImportException>(() => Import());
            Assert.Empty(Directory.GetFiles(root));
            return;
        }

        var set = Import();
        var file = set.FindFile("PPSA00001/sdimg_SAVE/data.bin")!;
        Assert.Equal(largeSize, file.Length);
        using var stream = file.OpenRead();
        Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(stream));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Empty_files_count_toward_the_entry_limit(int count, bool accepted)
    {
        var archive = TestZips.Build(Enumerable.Range(0, count).Select(i => ($"{i}.bin", Array.Empty<byte>())).ToArray());
        string root = TestZips.TempDir();
        DiskSaveSet Import() => SaveSetImporter.Import(
            [new ImportSource("empty-files.zip", () => new MemoryStream(archive))], root,
            new ImportLimits { MaxEntries = 2, MaxTotalBytes = 0 });

        if (accepted) Assert.Equal(count, Assert.Single(Import().Directories).Files.Count);
        else
        {
            Assert.Throws<ImportException>(() => Import());
            Assert.Empty(Directory.GetFiles(root));
        }
    }
}
