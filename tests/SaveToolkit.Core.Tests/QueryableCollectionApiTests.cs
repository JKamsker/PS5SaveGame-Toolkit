using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class QueryableCollectionApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] ExcludedRowKeys = ["50999"];
    private readonly HttpClient _client = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new QueryablePlugin()])));
    }).CreateClient();

    [Fact]
    public async Task SearchSortAndEditMiddleRow_StayBounded()
    {
        var (url, revision) = await OpenAsync();
        var page = await GetPageAsync($"{url}/collections/items?q=Item%20050000&limit=5&revision={revision}");
        Assert.Equal(1, page.GetProperty("filteredCount").GetInt64());
        Assert.Equal("50000", page.GetProperty("rows")[0].GetProperty("key").GetString());

        var valueId = page.GetProperty("rows")[0].GetProperty("cells").GetProperty("value").GetProperty("valueId").GetString();
        var edit = await _client.PostAsJsonAsync($"{url}/edits", new { revision, edits = new[] { new { valueId, value = "999" } } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        revision = (await edit.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("state").GetProperty("revision").GetInt64();

        var changed = await GetPageAsync($"{url}/collections/items?q=Item%20050000&limit=1&revision={revision}");
        Assert.Equal("999", changed.GetProperty("rows")[0].GetProperty("cells").GetProperty("value").GetProperty("value").GetString());

        var sorted = await GetPageAsync($"{url}/collections/items?sort=id&descending=true&offset=99999&limit=1&revision={revision}");
        Assert.Equal("0", sorted.GetProperty("rows")[0].GetProperty("key").GetString());

        var cursor = await GetPageAsync($"{url}/collections/items?cursor=100&limit=1&revision={revision}");
        Assert.Equal("100", cursor.GetProperty("rows")[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task QueryLimitsAndOldRevisionAreRejected()
    {
        var (url, revision) = await OpenAsync();
        var tooLarge = await _client.GetAsync($"{url}/collections/items?limit=201", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);

        var stale = await _client.GetAsync($"{url}/collections/items?limit=1&revision={revision + 1}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("stale", (await stale.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task QuerySelection_PreviewsUnloadedRows_AppliesAndUndoesTogether()
    {
        var ct = TestContext.Current.CancellationToken;
        var (url, revision) = await OpenAsync();
        var response = await _client.PostAsJsonAsync($"{url}/actions/plan", new
        {
            actionId = "increment", revision,
            scope = new { queryScope = new
            {
                collectionId = "items", revision,
                query = new { text = "Item", filters = new { name = "Item 050" }, sortColumn = "id", descending = true },
                excludedRowKeys = ExcludedRowKeys,
            } },
        }, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(999, preview.GetProperty("scope").GetProperty("rowKeys").GetArrayLength());
        Assert.Equal("50998", preview.GetProperty("scope").GetProperty("rowKeys")[0].GetString());
        Assert.Equal("items", preview.GetProperty("scope").GetProperty("queryScope").GetProperty("collectionId").GetString());
        Assert.Equal(999, preview.GetProperty("plan").GetProperty("changes").GetArrayLength());

        var applied = await _client.PostAsJsonAsync($"{url}/actions/apply", new { planId = preview.GetProperty("planId").GetString(), revision }, ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var state = (await applied.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state");
        var page = await GetPageAsync($"{url}/collections/items?q=Item%20050000&revision={state.GetProperty("revision").GetInt64()}");
        Assert.Equal("50001", page.GetProperty("rows")[0].GetProperty("cells").GetProperty("value").GetProperty("value").GetString());
        var undone = await _client.PostAsync(new Uri($"{url}/undo", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        state = (await undone.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state");
        Assert.False(state.GetProperty("canUndo").GetBoolean());
        page = await GetPageAsync($"{url}/collections/items?q=Item%20050000&revision={state.GetProperty("revision").GetInt64()}");
        Assert.Equal("50000", page.GetProperty("rows")[0].GetProperty("cells").GetProperty("value").GetProperty("value").GetString());
    }

    [Fact]
    public async Task QuerySelection_RejectsBroadAndStaleScopes()
    {
        var ct = TestContext.Current.CancellationToken;
        var (url, revision) = await OpenAsync();
        var response = await _client.PostAsJsonAsync($"{url}/actions/plan", new
        {
            actionId = "increment", revision,
            scope = new { queryScope = new { collectionId = "items", query = new { }, revision } },
        }, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(preview.TryGetProperty("planId", out _));
        Assert.Contains("5000 matching rows", preview.GetProperty("plan").GetProperty("errors")[0].GetProperty("message").GetString(), StringComparison.Ordinal);

        response = await _client.PostAsJsonAsync($"{url}/actions/plan", new
        {
            actionId = "increment", revision,
            scope = new { queryScope = new { collectionId = "items", query = new { text = "Item 050000" }, revision = revision + 1 } },
        }, ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        preview = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(preview.GetProperty("stale").GetBoolean());
        Assert.Equal("stale", preview.GetProperty("code").GetString());
    }

    private async Task<(string Url, long Revision)> OpenAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var zip = TestZips.Build(("PPSA99997/sdimg_SAVE/items.dat", QueryablePlugin.Create()));
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "files", "queryable.zip");
        var upload = await _client.PostAsync("/api/workspaces", form, ct);
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var wsId = workspace.GetProperty("id").GetString();
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var open = await _client.PostAsJsonAsync($"/api/workspaces/{wsId}/files/{fileId}/open", new { pluginId = "test.queryable" }, ct);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var document = await open.Content.ReadFromJsonAsync<JsonElement>(ct);
        return ($"/api/workspaces/{wsId}/documents/{document.GetProperty("id").GetString()}", document.GetProperty("state").GetProperty("revision").GetInt64());
    }

    private async Task<JsonElement> GetPageAsync(string url)
    {
        var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }
}
