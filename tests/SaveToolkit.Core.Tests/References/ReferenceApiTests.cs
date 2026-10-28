using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferenceApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        b.UseSetting("Toolkit:PackRoot", TestZips.TempDir());
        b.ConfigureTestServices(s => s.AddSingleton(new PluginCatalog([new ItemsPlugin(), new RawFilePlugin()])));
    }).CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> OpenItemsAsync()
    {
        var zip = TestZips.Build(($"{ItemsPlugin.TitleId}/sdimg_SAVE/items.dat", ItemsPlugin.Create(1, (1001, 3))));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zip), "files", "dump.zip");
        var ws = await (await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var wsId = ws.GetProperty("id").GetString();
        var fileId = ws.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var doc = await (await _client.PostAsJsonAsync($"/api/workspaces/{wsId}/files/{fileId}/open", new { }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        return $"/api/workspaces/{wsId}/documents/{doc.GetProperty("id").GetString()}";
    }

    private Task<HttpResponseMessage> UploadPackAsync(byte[] zip)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(content, "file", "pack.zip");
        return _client.PostAsync(new Uri("/api/reference-packs", UriKind.Relative), form, Ct);
    }

    [Fact]
    public async Task Sources_ListEmbeddedDatasetsInUse_WithAttribution()
    {
        var doc = await OpenItemsAsync();

        var sources = await _client.GetFromJsonAsync<JsonElement>($"{doc}/sources", Ct);

        var items = sources.GetProperty("datasets").EnumerateArray().Single(d => d.GetProperty("datasetId").GetString() == "items");
        Assert.True(items.GetProperty("used").GetBoolean());
        Assert.Equal("test.items.en", items.GetProperty("packId").GetString());
        Assert.Equal("Embedded", items.GetProperty("origin").GetString());
        var attribution = items.GetProperty("attribution");
        Assert.Equal("PS5 Save Game Toolkit contributors", attribution.GetProperty("author").GetString());
        Assert.Equal("2026-10-28", attribution.GetProperty("retrieved").GetString());
        Assert.StartsWith("https://", attribution.GetProperty("licenseUrl").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_Lookup_AndAsset_AreServedForTheDocument()
    {
        var doc = await OpenItemsAsync();

        var page = await _client.GetFromJsonAsync<JsonElement>($"{doc}/references/items?category=consumable&limit=1", Ct);
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal("1001", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("key").GetString());

        var detail = await _client.GetFromJsonAsync<JsonElement>($"{doc}/references/items/record?key=2001", Ct);
        Assert.Equal("Bronze Sword", detail.GetProperty("record").GetProperty("name").GetString());
        Assert.Equal("items", detail.GetProperty("dataset").GetProperty("id").GetString());
        Assert.NotNull(detail.GetProperty("dataset").GetProperty("attribution").GetProperty("license").GetString());

        var asset = await _client.GetAsync(new Uri($"{doc}/reference-assets?id={Uri.EscapeDataString("test.items.en:icons/potion.png")}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal("image/png", asset.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", asset.Headers.GetValues("X-Content-Type-Options").Single());

        var missing = await _client.GetAsync(new Uri($"{doc}/reference-assets?id=test.items.en:pack.json", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var unknownCatalog = await _client.GetAsync(new Uri($"{doc}/references/weapons", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, unknownCatalog.StatusCode);
    }

    [Fact]
    public async Task ImportPack_ListAndDelete()
    {
        var response = await UploadPackAsync(PackBuilder.WithItems().Zip());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/reference-packs", Ct);
        Assert.Contains(list.GetProperty("packs").EnumerateArray(), p => p.GetProperty("id").GetString() == "user.items");

        var doc = await OpenItemsAsync();
        var page = await _client.GetFromJsonAsync<JsonElement>($"{doc}/references/items?q=1001", Ct);
        Assert.Equal("User Potion", page.GetProperty("items")[0].GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(new Uri("/api/reference-packs/user.items", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync(new Uri("/api/reference-packs/test.items.en", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task ImportFromAnotherSite_IsRejected()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(PackBuilder.WithItems().Zip()), "file", "pack.zip");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/reference-packs", UriKind.Relative)) { Content = form };
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await _client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/reference-packs", Ct);
        Assert.DoesNotContain(list.GetProperty("packs").EnumerateArray(), p => p.GetProperty("id").GetString() == "user.items");
    }

    [Fact]
    public async Task ImportInvalidPack_Returns422WithReason()
    {
        var pack = PackBuilder.WithItems() with { Mutate = m => m["datasets"]![0]!["files"]![0]!["sha256"] = new string('0', 64) };

        var response = await UploadPackAsync(pack.Zip());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Contains("hash mismatch", body.GetProperty("error").GetString(), StringComparison.Ordinal);
    }
}
