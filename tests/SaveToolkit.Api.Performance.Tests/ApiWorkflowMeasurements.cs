using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Plugins;

namespace SaveToolkit.Api.Performance.Tests;

/// <summary>In-process HTTP characterization. Timings are diagnostics, never regression gates.</summary>
[Collection(ApiMeasurementCollection.Name)]
public sealed class ApiWorkflowMeasurements(ITestOutputHelper output)
{
    private const int SyntheticRows = 50_000;

    [Fact]
    public async Task LargeGenericJsonWorkflow()
    {
        using var host = new ApiPerformanceHost();
        var measurement = new ApiMeasurement(output, "generic-json-50k");
        // Warm routing, JSON metadata, middleware and discovery outside measured operations.
        _ = await host.GetAsync("/api/plugins");
        var bytes = Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Range(0, SyntheticRows)) + "]");
        var workspace = await measurement.RunAsync("import", () => host.ImportAsync(new MemoryStream(bytes), "large.json"));
        var ws = WorkspaceUrl(workspace);
        var file = Files(workspace).Single();
        var opened = await measurement.RunAsync("open-first-view", () => host.PostAsync(
            $"{ws}/files/{Id(file)}/open", new { pluginId = "format.json" }));
        var doc = $"{ws}/documents/{Id(opened)}";
        await ExerciseReads(host, measurement, doc, opened);

        // The scalar binding is published by the collection page, rather than the lazy root view.
        var table = Nodes(opened.GetProperty("view")).First(n => n.TryGetProperty("collectionId", out _));
        var page = await host.GetAsync($"{doc}/collections?collectionId={Uri.EscapeDataString(table.GetProperty("collectionId").GetString()!)}&limit=20");
        var field = Nodes(page).First(n => n.TryGetProperty("valueId", out _) && n.TryGetProperty("value", out _));
        var valueId = field.GetProperty("valueId").GetString();
        var edit = await measurement.RunAsync("single-field-edit", () => host.PostAsync($"{doc}/edits",
            new { edits = new[] { new { valueId, value = new { kind = "union", type = "number", value = 999999 } } }, revision = 0 }));
        var revision = edit.GetProperty("state").GetProperty("revision").GetInt64();
        var preview = await measurement.RunAsync("action-preview", () => host.PostAsync($"{doc}/actions/plan",
            new { actionId = "reset-imported", scope = new { rowKeys = new[] { valueId } }, revision }));
        Assert.Single(preview.GetProperty("plan").GetProperty("changes").EnumerateArray());
        var applied = await measurement.RunAsync("action-apply", () => host.PostAsync($"{doc}/actions/apply",
            new { planId = preview.GetProperty("planId").GetString(), revision }));
        Assert.True(applied.GetProperty("ok").GetBoolean());
        await measurement.RunAsync("undo", () => host.PostAsync($"{doc}/undo", new { }));
        await measurement.RunAsync("redo", () => host.PostAsync($"{doc}/redo", new { }));
        await Export(host, measurement, ws);
    }

    [Theory]
    [InlineData("PPSA01911_saves_dec.zip")]
    [InlineData("PPSA08330_saves_dec.zip")]
    [InlineData("PPSA08664_saves_dec.zip")]
    [InlineData("PPSA03169_saves_dec.zip")]
    public async Task LargestRealArchiveWorkflow(string archive)
    {
        var artifacts = Environment.GetEnvironmentVariable("SAVETOOLKIT_ARTIFACTS");
        var path = Path.Combine(artifacts ?? "artifacts", archive);
        Assert.SkipUnless(File.Exists(path), $"Private archive unavailable: {archive}");
        using var host = new ApiPerformanceHost();
        var measurement = new ApiMeasurement(output, archive);
        _ = await host.GetAsync("/api/plugins");
        var workspace = await measurement.RunAsync("import-large-zip", () => host.ImportAsync(File.OpenRead(path), archive));
        var ws = WorkspaceUrl(workspace);
        var file = Files(workspace).Where(f => f.GetProperty("matches").EnumerateArray()
                .Any(m => m.GetProperty("pluginId").GetString() != RawFilePlugin.PluginId))
            .OrderByDescending(f => f.GetProperty("length").GetInt64()).First();
        measurement.Describe($"Largest recognized file: {file.GetProperty("path")} ({file.GetProperty("length")} B)");
        var opened = await measurement.RunAsync("open-first-view", () => host.PostAsync($"{ws}/files/{Id(file)}/open", new { }));
        measurement.Describe($"Plugin: {opened.GetProperty("plugin").GetProperty("id")}");
        await ExerciseReads(host, measurement, $"{ws}/documents/{Id(opened)}", opened);
        await Export(host, measurement, ws);
    }

    private static async Task ExerciseReads(ApiPerformanceHost host, ApiMeasurement measurement, string doc, JsonElement opened)
    {
        await measurement.RunAsync("repeat-view", () => host.GetAsync(doc));
        // Include lazy views when locating the largest published collection.
        var nodes = Nodes(opened.GetProperty("view")).ToList();
        foreach (var lazy in nodes.Where(n => n.TryGetProperty("lazyId", out _)).Take(12).ToArray())
        {
            var expanded = await host.GetAsync($"{doc}/nodes?id={Uri.EscapeDataString(lazy.GetProperty("lazyId").GetString()!)}");
            nodes.AddRange(Nodes(expanded));
        }
        var tables = nodes.Where(n => n.TryGetProperty("collectionId", out _)).ToArray();
        if (tables.Length > 0)
        {
            var table = tables.OrderByDescending(t => t.GetProperty("totalCount").GetInt64()).First();
            var count = table.GetProperty("totalCount").GetInt32();
            var collection = table.GetProperty("collectionId").GetString()!;
            measurement.Describe($"Collection: {collection}; {count:N0} rows");
            var url = $"{doc}/collections?collectionId={Uri.EscapeDataString(collection)}&limit=20";
            await measurement.RunAsync("page-first", () => host.GetAsync(url));
            await measurement.RunAsync("page-deep", () => host.GetAsync($"{url}&offset={Math.Max(0, count - 20)}"));
            await measurement.RunAsync("search", () => host.GetAsync($"{url}&q=0"));
            var column = table.GetProperty("columns").EnumerateArray().FirstOrDefault();
            if (column.ValueKind != JsonValueKind.Undefined)
            {
                // GenericJson explicitly caps sorted matches at 10,000; narrow that large model.
                var filter = count > 10_000 ? "&q=4999" : "";
                var sorted = await measurement.RunAsync("sort", () => host.GetAsync($"{url}{filter}&sort={Uri.EscapeDataString(column.GetProperty("key").GetString()!)}&descending=true"));
                measurement.Describe($"Sort returned {sorted.GetProperty("rows").GetArrayLength()} rows");
            }
        }
        await measurement.RunAsync("sequential-read-8", async () =>
        {
            var results = new List<JsonElement>();
            for (var i = 0; i < 8; i++) results.Add(await host.GetAsync(doc));
            return results;
        });
        var concurrent = await measurement.RunAsync("concurrent-read-8", () => Task.WhenAll(Enumerable.Range(0, 8).Select(_ => host.GetAsync(doc))));
        Assert.All(concurrent, response => Assert.Equal(Id(opened), Id(response)));
    }

    private static Task<long> Export(ApiPerformanceHost host, ApiMeasurement measurement, string workspace) =>
        measurement.RunAsync("export-everything", async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{workspace}/export", UriKind.Relative))
            { Content = JsonContent.Create(new { scope = "Everything", mirrorBackups = false }) };
            using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ApiPerformanceHost.Ct);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(Stream.Null, ApiPerformanceHost.Ct);
            return response.Content.Headers.ContentLength ?? 0;
        });

    private static string Id(JsonElement element) => element.GetProperty("id").GetString()!;
    private static string WorkspaceUrl(JsonElement workspace) => $"/api/workspaces/{Id(workspace)}";
    private static IEnumerable<JsonElement> Files(JsonElement workspace) => workspace.GetProperty("titles").EnumerateArray()
        .SelectMany(t => t.GetProperty("directories").EnumerateArray()).SelectMany(d => d.GetProperty("files").EnumerateArray());

    private static IEnumerable<JsonElement> Nodes(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var child in Nodes(property.Value)) yield return child;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var child in Nodes(item)) yield return child;
    }
}
