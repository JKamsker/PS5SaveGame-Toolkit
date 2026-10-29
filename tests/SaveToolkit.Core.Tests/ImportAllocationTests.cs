using System.Security.Cryptography;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportAllocationTests(ITestOutputHelper output)
{
    private const int FileCount = 100;
    private const long PerFileBudget = 32_768;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManySmallFilesDoNotAllocateACopyBufferPerFile(bool zipped)
    {
        var content = new byte[512];
        var entries = Enumerable.Range(0, FileCount).Select(i => ($"data/{i}.bin", content)).ToArray();
        var archive = TestZips.Build(entries);
        ImportSource[] sources = zipped
            ? [new("many.zip", () => new MemoryStream(archive, writable: false))]
            : [.. entries.Select(entry => new ImportSource(entry.Item1, () => new MemoryStream(content, writable: false)))];
        _ = SaveSetImporter.Import(sources, TestZips.TempDir());
        var root = TestZips.TempDir();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var set = SaveSetImporter.Import(sources, root);
        var perFile = (GC.GetAllocatedBytesForCurrentThread() - before) / FileCount;

        output.WriteLine($"ZIP={zipped}: {perFile:N0} allocated bytes per 512-byte file.");
        Assert.Equal(FileCount, set.AllDiskFiles.Count());
        Assert.True(perFile <= PerFileBudget, $"Import allocated {perFile:N0} bytes per file.");
        Assert.All(set.AllDiskFiles, file => Assert.Equal(content, file.ReadAllBytes()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedBufferPreservesDifferentLengthsAndHashes(bool zipped)
    {
        var large = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        (string Path, byte[] Content)[] entries = [("large.bin", large), ("small.bin", [7]), ("empty.bin", [])];
        var archive = TestZips.Build(entries);
        ImportSource[] sources = zipped
            ? [new("different.zip", () => new MemoryStream(archive, writable: false))]
            : [.. entries.Select(entry => new ImportSource(entry.Path, () => new MemoryStream(entry.Content, writable: false)))];

        var set = SaveSetImporter.Import(sources, TestZips.TempDir());

        foreach (var (path, content) in entries)
        {
            var file = set.AllDiskFiles.Single(file => file.FileName == path);
            Assert.Equal(content, file.ReadAllBytes());
            Assert.Equal(content.Length, file.Length);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), file.Sha256);
        }
    }

    [Fact]
    public void FailedCopiesReturnTheBufferAndCleanUpBeforeTheNextImport()
    {
        const int repetitions = 10;
        var root = TestZips.TempDir();
        ImportSource[] sources = [new("data.bin", () => new FailingStream())];
        for (var i = 0; i < 5; i++) Fail();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < repetitions; i++) Fail();
        var perFailure = (GC.GetAllocatedBytesForCurrentThread() - before) / repetitions;

        output.WriteLine($"Failed import: {perFailure:N0} bytes per attempt.");
        Assert.True(perFailure <= PerFileBudget, $"Failed imports allocated {perFailure:N0} bytes each.");
        Assert.Empty(Directory.GetFiles(root));
        var recovered = SaveSetImporter.Import([new("data.bin", () => new MemoryStream(new byte[] { 9 }))], root);
        Assert.Equal(new byte[] { 9 }, Assert.Single(recovered.AllDiskFiles).ReadAllBytes());

        void Fail() => Assert.Throws<IOException>(() => SaveSetImporter.Import(sources, root));
    }

    private sealed class FailingStream : Stream
    {
        private bool _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read) throw new IOException("Input failed after a partial read.");
            _read = true;
            buffer[offset] = 42;
            return 1;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
