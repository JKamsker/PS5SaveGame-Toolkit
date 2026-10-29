using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Export;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpooledExportCancellationCollection
{
    public const string Name = "Spooled export cancellation";
}

[Collection(SpooledExportCancellationCollection.Name)]
public sealed class SpooledExportCancellationTests
{
    [Theory]
    [InlineData("before")]
    [InlineData("open")]
    [InlineData("read")]
    public void Cancelled_spooling_deletes_its_partial_archive_and_releases_source(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var original = MemorySaveSet.SingleFile("save.dat", new byte[1024 * 1024]);
        var file = new CancellingFile(original, cancellation, stage);
        var plan = new ExportPlan([], new Dictionary<string, ReadOnlyMemory<byte>>(), [file]);
        var before = Directory.GetFiles(Path.GetTempPath(), "SaveToolkit-export-*.zip").Order().ToArray();
        if (stage == "before") cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceExportResponse.Create(plan, "edited.zip", cancellation.Token));

        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "SaveToolkit-export-*.zip").Order());
        Assert.True(file.Source is null || file.Source.WasDisposed);
        // A successful retry proves cancellation retained neither a file handle nor shared state.
        var retry = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult>(
            WorkspaceExportResponse.Create(new ExportPlan([], new Dictionary<string, ReadOnlyMemory<byte>>(), [original]),
                "edited.zip", TestContext.Current.CancellationToken));
        var scratch = Assert.IsType<FileStream>(retry.FileStream).Name;
        retry.FileStream.Dispose();
        Assert.False(File.Exists(scratch));
    }

    private sealed class CancellingFile(ISaveFile inner, CancellationTokenSource cancellation, string stage) : ISaveFile
    {
        public CancellingStream? Source { get; private set; }
        public string Id => inner.Id;
        public ISaveDirectory Directory => inner.Directory;
        public string RelativePath => inner.RelativePath;
        public string PathInDirectory => inner.PathInDirectory;
        public string FileName => inner.FileName;
        public long Length => inner.Length;
        public byte[] ReadAllBytes() => inner.ReadAllBytes();
        public byte[] ReadHeader(int count) => inner.ReadHeader(count);
        public Stream OpenRead()
        {
            if (stage == "open") cancellation.Cancel();
            return Source = new CancellingStream(inner.ReadAllBytes(), () =>
            {
                if (stage == "read") cancellation.Cancel();
            });
        }
    }

    private sealed class CancellingStream(byte[] bytes, Action cancel) : MemoryStream(bytes)
    {
        public bool WasDisposed { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            cancel();
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
