using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class QueryableSemanticReviewApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ChangesTargetLoadsOpaqueRowAndRevertUsesRevisionOverHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
            builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new ReviewPlugin()])));
        });
        using var client = app.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)))), "files", "save.zip");
        using var upload = await client.PostAsync("/api/workspaces", form, ct);
        upload.EnsureSuccessStatusCode();
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var workspaceUrl = $"/api/workspaces/{workspace.GetProperty("id").GetString()}";
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        using var open = await client.PostAsJsonAsync($"{workspaceUrl}/files/{fileId}/open", new { pluginId = "test.review" }, ct);
        open.EnsureSuccessStatusCode();
        var document = await open.Content.ReadFromJsonAsync<JsonElement>(ct);
        var url = $"{workspaceUrl}/documents/{document.GetProperty("id").GetString()}";

        // Publish only one page before editing; review must discover the original on its own document.
        using var page = await client.GetAsync($"{url}/collections?collectionId=inventory%2F0&offset=250&limit=1&includeEmpty=true", ct);
        page.EnsureSuccessStatusCode();
        var valueId = SemanticQueryableDocument.ValueId(0, 250);
        using var edit = await client.PostAsJsonAsync($"{url}/edits", new { edits = new[] { new { valueId, value = "99" } }, revision = 0 }, ct);
        edit.EnsureSuccessStatusCode();
        var log = await client.GetFromJsonAsync<JsonElement>($"{url}/changes", ct);
        Assert.True(log.GetProperty("complete").GetBoolean());
        var change = Assert.Single(log.GetProperty("changes").EnumerateArray());
        Assert.True(change.GetProperty("canRevert").GetBoolean());
        Assert.Equal("250", change.GetProperty("originalValue").GetString());
        var target = change.GetProperty("target");
        var query = target.GetProperty("query");
        Assert.True(query.GetProperty("includeEmpty").GetBoolean());
        var collection = Uri.EscapeDataString(target.GetProperty("collectionId").GetString()!);
        var revision = log.GetProperty("revision").GetInt64();
        var loaded = await client.GetFromJsonAsync<JsonElement>($"{url}/collections?collectionId={collection}&offset={query.GetProperty("offset").GetInt32()}&limit={query.GetProperty("limit").GetInt32()}&includeEmpty=true&revision={revision}", ct);
        Assert.Contains(loaded.GetProperty("rows").EnumerateArray(), r => r.GetProperty("key").GetString() == target.GetProperty("rowKey").GetString());

        using var revert = await client.PostAsJsonAsync($"{url}/changes/revert", new { valueId, revision }, ct);
        Assert.Equal(HttpStatusCode.OK, revert.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"{url}/changes", ct)).GetProperty("changes").EnumerateArray());
        using var stale = await client.PostAsJsonAsync($"{url}/changes/revert", new { valueId, revision }, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    private sealed class ReviewPlugin : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.review", "Review fixture", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Format, "Review fixture");
        public ISaveDocument Open(ISaveFile file) => new SemanticQueryableDocument();
    }
}
