using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class OpaqueCollectionApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("gvas/properties")]
    [InlineData("literal%2Fitems")]
    [InlineData("inventory/#? + items")]
    public async Task QueryStringPreservesOpaqueCollectionIdentity(string collectionId)
    {
        var ct = TestContext.Current.CancellationToken;
        using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
            builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new QueryablePlugin(collectionId)])));
        });
        using var client = app.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestZips.Build(("PPSA99997/sdimg_SAVE/items.dat", QueryablePlugin.Create()))), "files", "items.zip");
        using var upload = await client.PostAsync("/api/workspaces", form, ct);
        upload.EnsureSuccessStatusCode();
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var workspaceUrl = $"/api/workspaces/{workspace.GetProperty("id").GetString()}";
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        using var open = await client.PostAsJsonAsync($"{workspaceUrl}/files/{fileId}/open", new { pluginId = "test.queryable" }, ct);
        open.EnsureSuccessStatusCode();
        var document = await open.Content.ReadFromJsonAsync<JsonElement>(ct);
        var url = $"{workspaceUrl}/documents/{document.GetProperty("id").GetString()}/collections";
        using var response = await client.GetAsync($"{url}?collectionId={Uri.EscapeDataString(collectionId)}&q=Item%20050000&limit=5&revision=0", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(collectionId, page.GetProperty("collectionId").GetString());
        Assert.Equal(1, page.GetProperty("filteredCount").GetInt64());
        Assert.Equal("50000", page.GetProperty("rows")[0].GetProperty("key").GetString());

        using var invalidLimit = await client.GetAsync($"{url}?collectionId={Uri.EscapeDataString(collectionId)}&limit=201", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
        using var stale = await client.GetAsync($"{url}?collectionId={Uri.EscapeDataString(collectionId)}&revision=1", ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
