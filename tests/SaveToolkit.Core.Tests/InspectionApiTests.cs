using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class InspectionApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        b.UseSetting("Toolkit:PackRoot", TestZips.TempDir());
        b.ConfigureTestServices(s => s.AddSingleton(new PluginCatalog([new InspectionPlugin()])));
    }).CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(string Path, JsonElement Document)> OpenAsync()
    {
        var zip = TestZips.Build(($"{ItemsPlugin.TitleId}/sdimg_SAVE/inspect.dat", new byte[] { 5, 7 }));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zip), "files", "inspection.zip");
        var ws = await (await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = ws.GetProperty("id").GetString();
        var fileId = ws.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var doc = await (await _client.PostAsJsonAsync($"/api/workspaces/{id}/files/{fileId}/open", new { }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        return ($"/api/workspaces/{id}/documents/{doc.GetProperty("id").GetString()}", doc);
    }

    [Fact]
    public async Task ReadOnlyInspection_PublishesOptInAndFloatDuration_AndKeepsRevision()
    {
        var (path, doc) = await OpenAsync();
        Assert.False(doc.GetProperty("canEdit").GetBoolean());
        var nodes = doc.GetProperty("view").GetProperty("children").EnumerateArray().ToArray();
        Assert.False(nodes.Single(n => n.TryGetProperty("actionId", out var id) && id.GetString() == "inspect").GetProperty("mutates").GetBoolean());
        Assert.True(nodes.Single(n => n.TryGetProperty("actionId", out var id) && id.GetString() == "legacy").GetProperty("mutates").GetBoolean());
        var timer = nodes.Single(n => n.TryGetProperty("valueId", out var id) && id.GetString() == "timer");
        Assert.Equal("Duration", timer.GetProperty("spec").GetProperty("format").GetString());
        Assert.Equal(1000, timer.GetProperty("spec").GetProperty("unitsPerSecond").GetDouble());
        Assert.Equal(61250.5, timer.GetProperty("value").GetDouble());

        var response = await _client.PostAsJsonAsync($"{path}/actions", new { actionId = "inspect", parameters = new { }, revision = 0 }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Potion: 5", result.GetProperty("message").GetString());
        Assert.Equal(0, result.GetProperty("state").GetProperty("revision").GetInt64());
        Assert.False(result.GetProperty("state").GetProperty("isDirty").GetBoolean());
        Assert.Empty(result.GetProperty("changes").EnumerateArray());

        foreach (var id in new[] { "legacy", "reported", "silent", "hidden", "structural", "throws" })
        {
            var rejected = await _client.PostAsJsonAsync($"{path}/actions", new { actionId = id, parameters = new { }, revision = 0 }, Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            var failure = await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(0, failure.GetProperty("state").GetProperty("revision").GetInt64());
            Assert.False(failure.GetProperty("state").GetProperty("isDirty").GetBoolean());
        }
    }

    [Fact]
    public async Task ReadOnlyReferences_CanBrowseFilteredSortedAttributedCandidates_ButCannotEdit()
    {
        var (path, _) = await OpenAsync();
        var query = $"{path}/reference-fields/candidates?valueId=item&category=consumable&sortBy=Name&descending=true&limit=1&revision=0";
        var page = await _client.GetFromJsonAsync<JsonElement>(query, Ct);
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal("Potion", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("label").GetString());
        Assert.Equal(3, page.GetProperty("sortKeys").GetArrayLength());
        var record = await _client.GetFromJsonAsync<JsonElement>($"{path}/reference-fields/record?valueId=item&rawId=1001&revision=0", Ct);
        Assert.Equal("PS5 Save Game Toolkit contributors", record.GetProperty("dataset").GetProperty("attribution").GetProperty("author").GetString());
        var rejected = await _client.PostAsJsonAsync($"{path}/edits", new { edits = new[] { new { valueId = "item", value = "1002" } }, revision = 0 }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        var catalog = await _client.GetFromJsonAsync<JsonElement>($"{path}/references/items?category=consumable&sortBy=Name&limit=1&offset=1", Ct);
        Assert.Equal("Potion", Assert.Single(catalog.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(3, catalog.GetProperty("sortKeys").GetArrayLength());
        foreach (var endpoint in new[] { "references/items?", "reference-fields/candidates?valueId=item&" })
        {
            var invalid = await _client.GetAsync(new Uri($"{path}/{endpoint}sortBy=999", UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }
}
