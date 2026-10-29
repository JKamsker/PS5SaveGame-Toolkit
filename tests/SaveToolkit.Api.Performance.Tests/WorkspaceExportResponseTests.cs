using System.IO.Compression;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Api.Performance.Tests;

[Collection(ApiMeasurementCollection.Name)]
public sealed class WorkspaceExportResponseTests
{
    [Fact]
    public async Task WritesOriginalAndOverrideBytesToAnAsynchronousNonseekableStream()
    {
        var original = new byte[128 * 1024];
        new Random(42).NextBytes(original);
        var set = SaveSetImporter.Import(
            [new ImportSource("original.bin", () => new MemoryStream(original)),
             new ImportSource("changed.bin", () => new MemoryStream(new byte[] { 1, 2 }))],
            Path.Combine(Path.GetTempPath(), "zip-streaming", Guid.NewGuid().ToString("N")));
        var files = set.AllDiskFiles.ToArray();
        var changed = files.Single(f => f.FileName == "changed.bin");
        var overrides = new Dictionary<string, ReadOnlyMemory<byte>> { [changed.RelativePath] = new byte[] { 3, 4, 5 } };
        var plan = new ExportPlan([], overrides, files);
        using var buffer = new MemoryStream();
        using var target = new AsyncOnlyStream(buffer);
        var result = Assert.IsType<FileStreamHttpResult>(WorkspaceExportResponse.Create(plan, "edited.zip"));
        var scratch = Assert.IsType<FileStream>(result.FileStream).Name;
        Assert.True(File.Exists(scratch));
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = target;
        await result.ExecuteAsync(context);
        Assert.False(File.Exists(scratch));
        Assert.False(target.WasDisposed);
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        // Title-less imports export under their imported paths, not the synthetic UNKNOWN grouping.
        Assert.Equal(files.Select(f => f.FileName).Order(), zip.Entries.Select(e => e.FullName).Order());
        foreach (var file in files)
        {
            using var entry = zip.GetEntry(file.FileName)!.Open();
            using var content = new MemoryStream();
            await entry.CopyToAsync(content, ApiPerformanceHost.Ct);
            Assert.Equal(file == changed ? new byte[] { 3, 4, 5 } : original, content.ToArray());
        }
    }

    [Fact]
    public async Task CancellationDuringTransferDeletesTheScratchArchive()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var result = Assert.IsType<FileStreamHttpResult>(WorkspaceExportResponse.Create(
            new ExportPlan([], new Dictionary<string, ReadOnlyMemory<byte>>(), []), "edited.zip"));
        var scratch = Assert.IsType<FileStream>(result.FileStream).Name;
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellation.Token };
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        await result.ExecuteAsync(context);
        Assert.False(File.Exists(scratch));
    }

    [Fact]
    public void FailedArchiveCreationDeletesItsScratchFile()
    {
        var set = SaveSetImporter.Import([new ImportSource("missing.bin", () => new MemoryStream(new byte[] { 1 }))],
            Path.Combine(Path.GetTempPath(), "zip-failure", Guid.NewGuid().ToString("N")));
        var file = Assert.Single(set.AllDiskFiles);
        string path;
        using (var source = Assert.IsType<FileStream>(file.OpenRead())) path = source.Name;
        File.Delete(path);
        var before = Directory.GetFiles(Path.GetTempPath(), "SaveToolkit-export-*.zip");
        Assert.Throws<FileNotFoundException>(() => WorkspaceExportResponse.Create(
            new ExportPlan([], new Dictionary<string, ReadOnlyMemory<byte>>(), [file]), "edited.zip"));
        Assert.Equal(before.Order(), Directory.GetFiles(Path.GetTempPath(), "SaveToolkit-export-*.zip").Order());
    }

    [Fact]
    public async Task FailedResponseTransferDeletesTheScratchArchive()
    {
        var result = Assert.IsType<FileStreamHttpResult>(WorkspaceExportResponse.Create(
            new ExportPlan([], new Dictionary<string, ReadOnlyMemory<byte>>(), []), "edited.zip"));
        var scratch = Assert.IsType<FileStream>(result.FileStream).Name;
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var buffer = new MemoryStream();
        using var target = new AsyncOnlyStream(buffer, failWrites: true);
        context.Response.Body = target;
        await Assert.ThrowsAsync<IOException>(() => result.ExecuteAsync(context));
        Assert.False(File.Exists(scratch));
    }

    [Fact]
    public async Task DeletingAWorkspaceWhileStreamingRetainsUnchangedFilesUntilCompletion()
    {
        using var host = new ApiPerformanceHost();
        var bytes = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(bytes);
        var workspace = await host.ImportAsync(new MemoryStream(bytes), "original.bin");
        var url = $"/api/workspaces/{workspace.GetProperty("id").GetString()}";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{url}/export", UriKind.Relative))
        { Content = JsonContent.Create(new { scope = "Everything" }) };
        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ApiPerformanceHost.Ct);
        response.EnsureSuccessStatusCode();
        using var removed = await host.Client.DeleteAsync(url, ApiPerformanceHost.Ct);
        removed.EnsureSuccessStatusCode();
        using var archive = new MemoryStream(await response.Content.ReadAsByteArrayAsync(ApiPerformanceHost.Ct));
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        using var entry = Assert.Single(zip.Entries).Open();
        using var actual = new MemoryStream();
        await entry.CopyToAsync(actual, ApiPerformanceHost.Ct);
        Assert.Equal(bytes, actual.ToArray());
    }

    private sealed class AsyncOnlyStream(Stream output, bool failWrites = false) : Stream
    {
        public bool WasDisposed { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !WasDisposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous write");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException("Synchronous write");
        public override void Flush() => throw new NotSupportedException("Synchronous flush");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            failWrites ? Task.FromException(new IOException("Injected write failure")) : output.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            failWrites ? ValueTask.FromException(new IOException("Injected write failure")) : output.WriteAsync(buffer, cancellationToken);
        public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
