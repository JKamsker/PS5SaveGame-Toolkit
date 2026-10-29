using System.Net.Http.Json;

namespace SaveToolkit.Api.Performance.Tests;

[Collection(ApiMeasurementCollection.Name)]
public sealed class HttpExportAllocationTests(ITestOutputHelper output)
{
    private const int PayloadBytes = 8 * 1024 * 1024;

    [Fact]
    public async Task HttpExportDoesNotBufferTheWholeArchive()
    {
        using var host = new ApiPerformanceHost();
        var bytes = new byte[PayloadBytes];
        new Random(42).NextBytes(bytes);
        var workspace = await host.ImportAsync(new MemoryStream(bytes), "random.bin");
        var url = $"/api/workspaces/{workspace.GetProperty("id").GetString()}/export";
        await ExportAsync();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        await ExportAsync();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        new ApiMeasurement(output, "8MiB-random").Record("http-export", allocated);
        Assert.True(allocated < PayloadBytes / 2, $"HTTP export allocated {allocated:N0} B.");

        async Task ExportAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative))
            { Content = JsonContent.Create(new { scope = "Everything", mirrorBackups = false }) };
            using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ApiPerformanceHost.Ct);
            response.EnsureSuccessStatusCode();
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            await response.Content.CopyToAsync(Stream.Null, ApiPerformanceHost.Ct);
        }
    }
}
