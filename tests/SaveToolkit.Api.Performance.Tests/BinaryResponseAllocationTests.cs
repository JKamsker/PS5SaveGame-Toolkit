namespace SaveToolkit.Api.Performance.Tests;

[Collection(ApiMeasurementCollection.Name)]
public sealed class BinaryResponseAllocationTests(ITestOutputHelper output)
{
    private const int LargePayloadBytes = 8 * 1024 * 1024;
    private const int BlobPageBytes = 64 * 1024;
    private const int RequestCount = 8;

    [Theory]
    [InlineData("download")]
    [InlineData("assets?id=image")]
    [InlineData("blobs?id=blob&offset=0&count=65536")]
    public async Task BinaryResponsesDoNotCloneTheSessionSnapshotAgain(string route)
    {
        var plugin = new LiveBufferPlugin(new byte[LargePayloadBytes]);
        using var host = new ApiPerformanceHost(plugin);
        var workspace = await host.ImportAsync(new MemoryStream(plugin.Bytes), "large.bin");
        var ws = $"/api/workspaces/{workspace.GetProperty("id").GetString()}";
        var file = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0];
        var opened = await host.PostAsync($"{ws}/files/{file.GetProperty("id").GetString()}/open", new { pluginId = plugin.Info.Id });
        var doc = $"{ws}/documents/{opened.GetProperty("id").GetString()}";
        await host.PostAsync($"{doc}/edits", new { edits = new[] { new { valueId = "value", value = 1 } } });
        var payloadBytes = route.StartsWith("blobs", StringComparison.Ordinal) ? BlobPageBytes : LargePayloadBytes;
        await ReadAsync();
        await ReadAsync();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < RequestCount; i++) await ReadAsync();
        var allocated = (GC.GetTotalAllocatedBytes(precise: true) - before) / RequestCount;
        new ApiMeasurement(output, route).Record("binary-response", allocated);
        // TestServer also allocates transfer buffers. Two complete payloads is still below
        // the old snapshot + clone + transfer cost, with room for one snapshot and transport.
        var limit = payloadBytes * 2;
        Assert.True(allocated < limit, $"{route} allocated {allocated:N0} B per request; limit {limit:N0} B.");

        async Task ReadAsync()
        {
            using var response = await host.Client.GetAsync($"{doc}/{route}", HttpCompletionOption.ResponseHeadersRead, ApiPerformanceHost.Ct);
            response.EnsureSuccessStatusCode();
            Assert.Equal(payloadBytes, response.Content.Headers.ContentLength);
            await response.Content.CopyToAsync(Stream.Null, ApiPerformanceHost.Ct);
        }
    }
}
