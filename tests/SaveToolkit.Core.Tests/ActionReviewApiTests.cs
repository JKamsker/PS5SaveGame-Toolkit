using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class ActionReviewApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string[] SelectedA = ["a"];
    private static readonly string[] DuplicateA = ["a", "a"];
    private readonly HttpClient _client = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new PlanningPlugin()])));
    }).CreateClient();

    [Fact]
    public async Task PlanApplyReviewRevertUndoAndStale_OverHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        var url = await OpenAsync(ct);
        var response = await _client.PostAsJsonAsync($"{url}/actions/plan", new { actionId = "refill", scope = new { rowKeys = SelectedA }, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = preview.GetProperty("planId").GetString();
        Assert.Equal(2, preview.GetProperty("plan").GetProperty("changes").GetArrayLength());
        Assert.Equal(0, preview.GetProperty("revision").GetInt64());
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement>($"{url}/changes", ct)).GetProperty("changes").EnumerateArray());

        var apply = await _client.PostAsJsonAsync($"{url}/actions/apply", new { planId = id, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        var applied = await apply.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, applied.GetProperty("state").GetProperty("revision").GetInt64());
        var log = await _client.GetFromJsonAsync<JsonElement>($"{url}/changes", ct);
        Assert.True(log.GetProperty("complete").GetBoolean());
        Assert.Equal(2, log.GetProperty("changes").GetArrayLength());
        Assert.Equal("2", log.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("valueId").GetString() == "item/a").GetProperty("originalValue").GetString());

        var stale = await _client.PostAsJsonAsync($"{url}/actions/apply", new { planId = id, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(staleBody.GetProperty("stale").GetBoolean());
        Assert.Equal("stale", staleBody.GetProperty("code").GetString());

        var revert = await _client.PostAsJsonAsync($"{url}/changes/revert", new { valueId = "item/a", revision = 1 }, ct);
        Assert.Equal(HttpStatusCode.OK, revert.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement>($"{url}/changes", ct)).GetProperty("changes").EnumerateArray());
        var undo = await _client.PostAsync(new Uri($"{url}/undo", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Equal(2, (await _client.GetFromJsonAsync<JsonElement>($"{url}/changes", ct)).GetProperty("changes").GetArrayLength());

        response = await _client.PostAsJsonAsync($"{url}/actions/plan", new { actionId = "refill", revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task InvalidScopeAndDirectPlannedInvocation_AreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var url = await OpenAsync(ct);
        var direct = await _client.PostAsJsonAsync($"{url}/actions", new { actionId = "refill" }, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, direct.StatusCode);
        var response = await _client.PostAsJsonAsync($"{url}/actions/plan", new { actionId = "refill", scope = new { rowKeys = DuplicateA } }, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(preview.TryGetProperty("planId", out _));
        Assert.NotEmpty(preview.GetProperty("plan").GetProperty("errors").EnumerateArray());
    }

    private async Task<string> OpenAsync(CancellationToken ct)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 0 }));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zip), "files", "dump.zip");
        var upload = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, ct);
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var workspaceId = workspace.GetProperty("id").GetString();
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var opened = await _client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/files/{fileId}/open", new { pluginId = "test.planning" }, ct);
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(ct);
        return $"/api/workspaces/{workspaceId}/documents/{document.GetProperty("id").GetString()}";
    }
}
