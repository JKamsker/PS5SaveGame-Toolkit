using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferenceFieldApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        b.UseSetting("Toolkit:PackRoot", TestZips.TempDir());
        b.ConfigureTestServices(s => s.AddSingleton(new PluginCatalog([new ItemsPlugin()])));
    }).CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> OpenAsync()
    {
        var zip = TestZips.Build(($"{ItemsPlugin.TitleId}/sdimg_SAVE/items.dat", ItemsPlugin.Create(1, (9999, 3))));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zip), "files", "items.zip");
        var ws = await (await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var wsId = ws.GetProperty("id").GetString();
        var fileId = ws.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var doc = await (await _client.PostAsJsonAsync($"/api/workspaces/{wsId}/files/{fileId}/open", new { }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var field = doc.GetProperty("view").GetProperty("children")[0].GetProperty("rows")[0].GetProperty("cells").GetProperty("item");
        Assert.Equal("reference", field.GetProperty("spec").GetProperty("kind").GetString());
        Assert.Equal("9999", field.GetProperty("value").GetString());
        return $"/api/workspaces/{wsId}/documents/{doc.GetProperty("id").GetString()}";
    }

    [Fact]
    public async Task Candidates_AreBounded_Paged_Filtered_AndAttributed()
    {
        var doc = await OpenAsync();
        var path = $"{doc}/reference-fields/candidates?valueId=items%2F0%2Fid&revision=0";
        var page = await _client.GetFromJsonAsync<JsonElement>($"{path}&category=consumable&offset=1&limit=1", Ct);
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("1002", item.GetProperty("rawId").GetString());
        Assert.Equal("Ether", item.GetProperty("label").GetString());
        Assert.Equal("items", item.GetProperty("dataset").GetProperty("id").GetString());
        Assert.Equal("icons", item.GetProperty("assetDataset").GetProperty("id").GetString());
        Assert.Equal("PS5 Save Game Toolkit contributors", item.GetProperty("dataset").GetProperty("attribution").GetProperty("author").GetString());
        var capped = await _client.GetFromJsonAsync<JsonElement>($"{path}&q=potion&offset=-2&limit=9999", Ct);
        Assert.Equal(200, capped.GetProperty("limit").GetInt32());
        Assert.Equal(0, capped.GetProperty("offset").GetInt32());
        Assert.Single(capped.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task UnknownAndIneligibleIds_AreVisible_ButEditsAreRejected()
    {
        var doc = await OpenAsync();
        var record = $"{doc}/reference-fields/record?valueId=items%2F0%2Fid&rawId=";
        var unknown = await _client.GetFromJsonAsync<JsonElement>($"{record}9999", Ct);
        Assert.Equal("9999", unknown.GetProperty("rawId").GetString());
        Assert.False(unknown.TryGetProperty("label", out _));
        var ineligible = await _client.GetFromJsonAsync<JsonElement>($"{record}2001", Ct);
        Assert.False(ineligible.GetProperty("eligibility").GetProperty("selectable").GetBoolean());
        Assert.Contains("consumables", ineligible.GetProperty("eligibility").GetProperty("reasons")[0].GetString(), StringComparison.Ordinal);

        foreach (var rawId in new[] { "missing", "2001" })
        {
            var result = await _client.PostAsJsonAsync($"{doc}/edits", new { edits = new[] { new { valueId = "items/0/id", value = rawId } }, revision = 0 }, Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, result.StatusCode);
        }

        var edit = await _client.PostAsJsonAsync($"{doc}/edits", new { edits = new[] { new { valueId = "items/0/id", value = "1002" } }, revision = 0 }, Ct);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var changes = (await edit.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("changes");
        Assert.Equal("9999", changes[0].GetProperty("oldValue").GetString());
    }

    [Fact]
    public async Task ActionParameters_UseTheSameCandidatePolicy_AndValidation()
    {
        var doc = await OpenAsync();
        var page = await _client.GetFromJsonAsync<JsonElement>($"{doc}/reference-fields/candidates?actionId=replace-first&valueId=replacement&q=Sword", Ct);
        Assert.False(Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("eligibility").GetProperty("selectable").GetBoolean());
        foreach (var rawId in new[] { "missing", "2001" })
        {
            var rejected = await _client.PostAsJsonAsync($"{doc}/actions", new { actionId = "replace-first", parameters = new { replacement = rawId }, revision = 0 }, Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        }

        var accepted = await _client.PostAsJsonAsync($"{doc}/actions", new { actionId = "replace-first", parameters = new { replacement = "1002" }, revision = 0 }, Ct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var result = await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(result.GetProperty("structureChanged").GetBoolean());
        Assert.Equal(1, result.GetProperty("state").GetProperty("revision").GetInt64());
        var stale = await _client.GetAsync(new Uri($"{doc}/reference-fields/candidates?actionId=replace-first&valueId=replacement&revision=0", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBatch = await _client.PostAsJsonAsync($"{doc}/reference-fields/records", new
        {
            pairs = new[] { new { fieldId = "replacement", recordKey = "1002", actionId = "replace-first" } },
            revision = 0,
        }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, staleBatch.StatusCode);
        var problem = await staleBatch.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("stale", problem.GetProperty("code").GetString());
        Assert.False(problem.TryGetProperty("results", out _));
    }

    [Fact]
    public async Task UnpublishedFieldsAndStaleRevisions_AreRejected()
    {
        var doc = await OpenAsync();
        var stale = await _client.GetAsync(new Uri($"{doc}/reference-fields/candidates?valueId=items%2F0%2Fid&revision=100", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("stale", (await stale.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString());
        var missing = await _client.GetAsync(new Uri($"{doc}/reference-fields/record?valueId=not-published&rawId=1001", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
