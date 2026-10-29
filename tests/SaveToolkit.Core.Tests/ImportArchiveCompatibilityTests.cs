using System.IO.Compression;
using System.Text;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportArchiveCompatibilityTests
{
    [Theory]
    [InlineData(CompressionLevel.NoCompression, false)]
    [InlineData(CompressionLevel.Optimal, false)]
    [InlineData(CompressionLevel.NoCompression, true)]
    [InlineData(CompressionLevel.Optimal, true)]
    public void Stored_and_deflated_archives_support_seekable_and_streaming_writers(CompressionLevel compression, bool streaming)
    {
        using var buffer = new MemoryStream();
        using (var output = streaming ? new NonSeekingWriteStream(buffer) : (Stream)buffer)
        {
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            zip.Comment = "Archive comment from a different ZIP writer.";
            using var entry = zip.CreateEntry("./PPSA00001/sdimg_SAVE/保存🎮.bin", compression).Open();
            entry.Write([1, 2, 3]);
        }

        var set = Import(buffer.ToArray());
        var file = Assert.Single(Assert.Single(set.Directories).Files);
        Assert.Equal("保存🎮.bin", file.PathInDirectory);
        Assert.Equal([1, 2, 3], file.ReadAllBytes());
    }

    [Fact]
    public void Legacy_zip_names_use_the_standard_code_page_instead_of_replacement_characters()
    {
        using var buffer = new MemoryStream();
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(437)!;
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: encoding))
        {
            using var entry = zip.CreateEntry("PPSA00001/sdimg_SAVE/café.bin").Open();
            entry.Write([4, 5]);
        }

        var set = Import(buffer.ToArray());
        var file = Assert.Single(Assert.Single(set.Directories).Files);
        Assert.Equal("café.bin", file.PathInDirectory);
        Assert.Equal([4, 5], file.ReadAllBytes());
    }

    [Fact]
    public void Every_incomplete_prefix_of_an_archive_is_rejected_and_rolled_back()
    {
        byte[] archive = TestZips.Build(("PPSA00001/sdimg_SAVE/a.bin", [1, 2, 3]), ("PPSA00001/sdimg_SAVE/b.bin", [4]));
        string root = TestZips.TempDir();
        for (int length = 0; length < archive.Length; length++)
        {
            int prefix = length;
            Assert.Throws<InvalidDataException>(() => SaveSetImporter.Import(
                [new ImportSource("partial.zip", () => new MemoryStream(archive, 0, prefix))], root));
            Assert.Empty(Directory.GetFiles(root));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_archives_and_directory_only_archives_have_a_clear_error(bool directory)
    {
        byte[] archive = directory ? TestZips.Build(("PPSA00001/sdimg_SAVE/", [])) : TestZips.Build();
        var error = Assert.Throws<ImportException>(() => Import(archive));
        Assert.Contains("No save files", error.Message, StringComparison.Ordinal);
    }

    private static DiskSaveSet Import(byte[] archive) => SaveSetImporter.Import(
        [new ImportSource("tools.ZIP", () => new MemoryStream(archive))], TestZips.TempDir());

    private sealed class NonSeekingWriteStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
