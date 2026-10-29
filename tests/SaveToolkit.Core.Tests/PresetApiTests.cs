using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class PresetApiTests(IsolatedApiFactory factory) : IClassFixture<IsolatedApiFactory>
{
    private static readonly string[] CounterTitle = ["PPSA99999"];
    private static readonly string[] OtherTitle = ["OTHER"];
    private static readonly string[] SelectedRows = ["a"];
    private readonly HttpClient _client = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir()).UseSetting("Toolkit:PresetRoot", TestZips.TempDir());
        builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new PlanningPlugin()])));
    }).CreateClient();

    [Fact]
    public async Task PresetPreviewAndApplyUseF6RevisionAndOneUndoUnit()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        var create = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Refill",
            compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = CounterTitle, gameBuild = "r19" },
            actions = new[] { new { actionId = "refill", parameters = new { value = 7 } } },
        }, ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var preset = await create.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = preset.GetProperty("id").GetString();

        var previewResponse = await _client.PostAsJsonAsync($"{document}/presets/{id}/preview", new { revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, previewResponse.StatusCode); // build is unknown to the fixture

        // A null build is compatible; this proves preview does not mutate the document.
        var update = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Refill compatible",
            compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = CounterTitle },
            actions = new[] { new { actionId = "refill", parameters = new { value = 7 } } },
        }, ct);
        var compatible = await update.Content.ReadFromJsonAsync<JsonElement>(ct);
        var compatibleId = compatible.GetProperty("id").GetString();
        var preview = await _client.PostAsJsonAsync($"{document}/presets/{compatibleId}/preview", new { revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(0, previewBody.GetProperty("preview").GetProperty("revision").GetInt64());
        var planId = previewBody.GetProperty("preview").GetProperty("planId").GetString();
        var apply = await _client.PostAsJsonAsync($"{document}/presets/{compatibleId}/apply", new { planId, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        var applied = await apply.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, applied.GetProperty("state").GetProperty("revision").GetInt64());
        Assert.True(applied.GetProperty("state").GetProperty("canUndo").GetBoolean());
    }

    [Fact]
    public async Task MismatchAndDeleteDoNotTouchDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        var create = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Other title",
            compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = OtherTitle },
            actions = new[] { new { actionId = "refill", parameters = new { value = 7 } } },
        }, ct);
        var preset = await create.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = preset.GetProperty("id").GetString();
        var rejected = await _client.PostAsJsonAsync($"{document}/presets/{id}/preview", new { revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/presets/{id}", ct)).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement[]>("/api/presets", ct))!);
    }

    [Fact]
    public async Task CaptureRouteUsesDocumentCompatibilityAndNeverStoresRowKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        var captured = await _client.PostAsJsonAsync($"{document}/presets/capture", new
        {
            name = "Captured",
            actionId = "refill",
            parameters = new { value = 8 },
            scopeKind = "document",
            selectedRowKeys = SelectedRows,
        }, ct);
        Assert.Equal(HttpStatusCode.OK, captured.StatusCode);
        var body = await captured.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("PPSA99999", body.GetProperty("compatibility").GetProperty("titleIds")[0].GetString());
        Assert.False(body.GetProperty("actions")[0].TryGetProperty("selectedRowKeys", out _));
        Assert.Equal("document", body.GetProperty("actions")[0].GetProperty("scopeKind").GetString());
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
