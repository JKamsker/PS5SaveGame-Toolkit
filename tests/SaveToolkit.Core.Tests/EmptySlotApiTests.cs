using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class EmptySlotApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new QueryablePlugin(emptySlots: true)])));
    }).CreateClient();

    [Fact]
    public async Task MetadataQueriesAndF6ScopesTransportEmptyRowVisibility()
    {
        var ct = TestContext.Current.CancellationToken;
        var zip = TestZips.Build(("PPSA99997/sdimg_SAVE/items.dat", QueryablePlugin.Create()));
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "files", "slots.zip");
        var upload = await _client.PostAsync("/api/workspaces", form, ct);
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var workspaceUrl = $"/api/workspaces/{workspace.GetProperty("id").GetString()}";
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var opened = await _client.PostAsJsonAsync($"{workspaceUrl}/files/{fileId}/open", new { pluginId = "test.queryable" }, ct);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Contains("\"hideByDefault\":true", document.GetRawText());
        Assert.Contains("\"labelColumnKey\":\"name\"", document.GetRawText());
        var url = $"{workspaceUrl}/documents/{document.GetProperty("id").GetString()}";

        foreach (var includeEmpty in new bool?[] { null, false, true })
        {
            var response = await _client.GetAsync($"{url}/collections/items?q=Item%20050&limit=2" +
                (includeEmpty is null ? "" : $"&includeEmpty={includeEmpty.Value.ToString().ToLowerInvariant()}"), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            var shown = includeEmpty == true;
            Assert.Equal(shown ? 1000 : 500, page.GetProperty("filteredCount").GetInt64());
            Assert.Equal(shown ? 0 : 500, page.GetProperty("hiddenEmptyCount").GetInt64());
            Assert.Equal(shown ? "50000" : "50001", page.GetProperty("rows")[0].GetProperty("key").GetString());
            Assert.Equal(shown, page.GetProperty("rows")[0].GetProperty("isEmpty").GetBoolean());
            var planned = await _client.PostAsJsonAsync($"{url}/actions/plan", new
            {
                actionId = "increment", scope = new { queryScope = new
                {
                    collectionId = "items", revision = 0, query = new { text = "Item 050", includeEmpty },
                } },
            }, ct);
            Assert.Equal(HttpStatusCode.OK, planned.StatusCode);
            var preview = await planned.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(shown ? 1000 : 500, preview.GetProperty("scope").GetProperty("rowKeys").GetArrayLength());
            Assert.Equal(shown, preview.GetProperty("scope").GetProperty("queryScope").GetProperty("query").GetProperty("includeEmpty").GetBoolean());
        }

        var invalid = await _client.GetAsync($"{url}/collections/items?includeEmpty=perhaps", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
