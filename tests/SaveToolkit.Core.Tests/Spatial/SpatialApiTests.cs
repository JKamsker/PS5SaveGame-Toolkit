using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Spatial;

public sealed class SpatialApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        b.UseSetting("Toolkit:PackRoot", TestZips.TempDir());
        b.ConfigureTestServices(s => s.AddSingleton(new PluginCatalog([new SpatialPlugin(), new RawFilePlugin()])));
    }).CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SpatialPlugin_PublishesGraphLicensedBackgroundAndExternalOnlyRegion_WithWorkingFieldLinks()
    {
        var zip = TestZips.Build(($"{SpatialPlugin.TitleId}/sdimg_SAVE/spatial.dat", SpatialPlugin.Create()));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zip), "files", "spatial.zip");
        using var import = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, Ct);
        import.EnsureSuccessStatusCode();
        var workspace = await import.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var wsId = workspace.GetProperty("id").GetString();
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        using var opened = await _client.PostAsJsonAsync($"/api/workspaces/{wsId}/files/{fileId}/open", new { }, Ct);
        opened.EnsureSuccessStatusCode();
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var path = $"/api/workspaces/{wsId}/documents/{document.GetProperty("id").GetString()}";
        var root = JsonSerializer.Deserialize<ViewNode>(document.GetProperty("view"), ToolkitJson.Options);
        var children = Assert.IsType<SectionNode>(root).Children;
        Assert.IsType<GraphNode>(children[0]);
        var map = Assert.IsType<MapNode>(children[1]);
        Assert.NotNull(map.Background);
        Assert.True(map.Background.EmbeddingPermitted);
        Assert.Equal("CC0-1.0", map.Background.Attribution.License);
        var external = Assert.IsType<MapNode>(children[2]);
        Assert.NotNull(external.ExternalSource);
        Assert.Null(external.Background);
        Assert.All(external.Markers, marker => { Assert.Null(marker.X); Assert.Null(marker.Y); });

        using var asset = await _client.GetAsync(new Uri($"{path}/reference-assets?id={Uri.EscapeDataString(map.Background.AssetId)}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("image/png", asset.Content.Headers.ContentType!.MediaType);
        Assert.True((await asset.Content.ReadAsByteArrayAsync(Ct)).AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        var sources = await _client.GetFromJsonAsync<JsonElement>($"{path}/sources", Ct);
        Assert.Contains(sources.GetProperty("datasets").EnumerateArray(), d =>
            d.GetProperty("packId").GetString() == "test.spatial" && d.GetProperty("used").GetBoolean());

        using var edited = await _client.PostAsJsonAsync($"{path}/edits", new
        {
            edits = new[] { new { valueId = map.Markers[0].Target!.Id, value = true } },
        }, Ct);
        edited.EnsureSuccessStatusCode();
        var result = await edited.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.True(result.GetProperty("structureChanged").GetBoolean());
        var updated = await _client.GetFromJsonAsync<JsonElement>(path, Ct);
        Assert.Equal("Found", updated.GetProperty("view").GetProperty("children")[1].GetProperty("markers")[0].GetProperty("status").GetString());
        using var undone = await _client.PostAsJsonAsync($"{path}/undo", new { }, Ct);
        undone.EnsureSuccessStatusCode();
        var restored = await _client.GetFromJsonAsync<JsonElement>(path, Ct);
        Assert.Equal("Missing", restored.GetProperty("view").GetProperty("children")[1].GetProperty("markers")[0].GetProperty("status").GetString());
    }
}
