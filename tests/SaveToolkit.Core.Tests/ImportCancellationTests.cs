using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class ImportCancellationTests
{
    [Fact]
    public void Precancelled_import_does_not_open_sources_or_create_storage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        string root = TestZips.TempDir();
        var source = new ImportSource("file.bin", () => throw new InvalidOperationException("Must not open."));

        var error = Assert.Throws<OperationCanceledException>(() =>
            SaveSetImporter.Import([source], root, null, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_while_copying_removes_only_import_blobs_and_closes_source(bool endOfStream)
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new CancellingStream(cancellation, endOfStream);
        string root = TestZips.TempDir();
        Directory.CreateDirectory(root);
        string sentinel = Path.Combine(root, "keep.txt");
        File.WriteAllBytes(sentinel, [9]);

        var error = Assert.Throws<OperationCanceledException>(() => SaveSetImporter.Import(
            [new ImportSource("first.bin", () => new MemoryStream([1])), new ImportSource("next.bin", () => source)],
            root, null, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, source.Reads);
        Assert.False(source.CanRead);
        Assert.Equal([sentinel], Directory.GetFiles(root));
        Assert.Equal([9], File.ReadAllBytes(sentinel));
        var retry = SaveSetImporter.Import([new ImportSource("retry.bin", () => new MemoryStream([7]))], root);
        Assert.Equal([7], Assert.Single(retry.AllDiskFiles).ReadAllBytes());
    }

    [Theory]
    [InlineData("next-source")]
    [InlineData("end-of-input")]
    [InlineData("archive-open")]
    public void Cancellation_at_input_boundaries_rolls_back_completed_files(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        string root = TestZips.TempDir();
        byte[] archive = TestZips.Build(("archive.bin", [2]));
        IEnumerable<ImportSource> Sources()
        {
            yield return new("first.bin", () => new MemoryStream([1]));
            if (stage != "archive-open") cancellation.Cancel();
            if (stage == "end-of-input") yield break;
            yield return stage == "archive-open"
                ? new("dump.zip", () => { cancellation.Cancel(); return new MemoryStream(archive); })
                : new("next.bin", () => throw new InvalidOperationException("Must not open next source."));
        }

        Assert.Throws<OperationCanceledException>(() => SaveSetImporter.Import(Sources(), root, null, cancellation.Token));

        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void Cancelled_workspace_import_does_not_publish_or_leave_scratch()
    {
        using var cancellation = new CancellationTokenSource();
        string root = TestZips.TempDir();
        using var store = new WorkspaceStore(new PluginCatalog([]), new() { DataRoot = root });
        var existing = store.Create("existing", [new ImportSource("keep.bin", () => new MemoryStream([9]))]);
        using var source = new CancellingStream(cancellation, endOfStream: false);

        Assert.Throws<OperationCanceledException>(() => store.Create("cancelled",
            [new ImportSource("file.bin", () => source)], cancellation.Token));

        Assert.Same(existing, Assert.Single(store.All));
        Assert.Equal([existing.Set.StorageRoot], Directory.GetDirectories(root));
        Assert.Equal([9], Assert.Single(existing.Set.AllDiskFiles).ReadAllBytes());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_during_zip_metadata_or_entry_read_closes_archive_and_removes_all_blobs(bool duringEntry)
    {
        using var cancellation = new CancellationTokenSource();
        byte[] bytes = new byte[200_000];
        new Random(1234).NextBytes(bytes);
        byte[] archive = TestZips.Build(("data.bin", bytes));
        using var source = new CancellingArchiveStream(archive, cancellation, duringEntry);
        string root = TestZips.TempDir();

        Assert.Throws<OperationCanceledException>(() => SaveSetImporter.Import(
            [new ImportSource("first.bin", () => new MemoryStream([1])), new ImportSource("dump.zip", () => source)],
            root, null, cancellation.Token));

        Assert.Equal(duringEntry, source.EntryRead);
        Assert.False(source.CanRead);
        Assert.Empty(Directory.GetFiles(root));
    }

    private sealed class CancellingArchiveStream(byte[] archive, CancellationTokenSource cancellation, bool duringEntry) : MemoryStream(archive)
    {
        private const int LocalHeaderBytes = 30;
        private const int EntryNameBytes = 8;
        public bool EntryRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            EntryRead = Position == LocalHeaderBytes + EntryNameBytes;
            if (!duringEntry || EntryRead) cancellation.Cancel();
            return base.Read(buffer, offset, count);
        }
    }

    private sealed class CancellingStream(CancellationTokenSource cancellation, bool endOfStream) : MemoryStream(new byte[200_000])
    {
        public int Reads { get; private set; }

        public override int Read(Span<byte> buffer)
        {
            Reads++;
            cancellation.Cancel();
            return endOfStream ? 0 : base.Read(buffer);
        }
    }
}
