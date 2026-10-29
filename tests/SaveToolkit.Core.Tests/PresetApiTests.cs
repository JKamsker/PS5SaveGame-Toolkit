using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Api.Infrastructure;
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
    public async Task ApplyRejectsAPlanTokenPresentedForAnotherCompatiblePreset()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        async Task<string?> Create(string name, int value)
        {
            var response = await _client.PostAsJsonAsync("/api/presets", new
            {
                name,
                compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = CounterTitle },
                actions = new[] { new { actionId = "refill", parameters = new { value } } },
            }, ct);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return body.GetProperty("id").GetString();
        }
        var first = await Create("First", 7);
        var second = await Create("Second", 8);
        var preview = await _client.PostAsJsonAsync($"{document}/presets/{first}/preview", new { revision = 0 }, ct);
        var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>(ct);
        var planId = previewBody.GetProperty("preview").GetProperty("planId").GetString();
        var swapped = await _client.PostAsJsonAsync($"{document}/presets/{second}/apply", new { planId, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.Conflict, swapped.StatusCode);
        var retry = await _client.PostAsJsonAsync($"{document}/presets/{first}/apply", new { planId, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
    }

    [Fact]
    public async Task ApplyRejectsRenamedOrDeletedPresetAfterPreview()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        var create = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Mutable",
            compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = CounterTitle },
            actions = new[] { new { actionId = "refill", parameters = new { value = 7 } } },
        }, ct);
        var preset = await create.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = preset.GetProperty("id").GetString();
        var preview = await _client.PostAsJsonAsync($"{document}/presets/{id}/preview", new { revision = 0 }, ct);
        var previewBody = await preview.Content.ReadFromJsonAsync<JsonElement>(ct);
        var planId = previewBody.GetProperty("preview").GetProperty("planId").GetString();

        Assert.Equal(HttpStatusCode.OK, (await _client.PatchAsJsonAsync($"/api/presets/{id}", new { name = "Changed" }, ct)).StatusCode);
        var renamedApply = await _client.PostAsJsonAsync($"{document}/presets/{id}/apply", new { planId, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.Conflict, renamedApply.StatusCode);

        var secondCreate = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Deleted",
            compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = CounterTitle },
            actions = new[] { new { actionId = "refill", parameters = new { value = 8 } } },
        }, ct);
        var second = await secondCreate.Content.ReadFromJsonAsync<JsonElement>(ct);
        var secondId = second.GetProperty("id").GetString();
        var secondPreview = await _client.PostAsJsonAsync($"{document}/presets/{secondId}/preview", new { revision = 0 }, ct);
        var secondBody = await secondPreview.Content.ReadFromJsonAsync<JsonElement>(ct);
        var secondPlanId = secondBody.GetProperty("preview").GetProperty("planId").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/presets/{secondId}", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync($"{document}/presets/{secondId}/apply", new { planId = secondPlanId, revision = 0 }, ct)).StatusCode);
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
    public async Task DocumentPresetRowsGateGenericShapeOnTheServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        var create = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Selected generic",
            compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = CounterTitle },
            actions = new[] { new { actionId = "refill", scopeKind = "selected", parameters = new { value = 7 } } },
        }, ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var rows = await _client.GetFromJsonAsync<JsonElement[]>($"{document}/presets", ct);
        var summary = Assert.Single(rows!).GetProperty("compatibility");
        Assert.False(summary.GetProperty("canPreview").GetBoolean());
        Assert.Equal("genericShapeUnsupported", summary.GetProperty("previewCode").GetString());
    }

    [Fact]
    public async Task GenericRowsUseTheCurrentPlanningActionIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        var create = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Unknown action",
            compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = CounterTitle },
            actions = new[] { new { actionId = "does-not-exist", parameters = new { } } },
        }, ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var rows = await _client.GetFromJsonAsync<JsonElement[]>($"{document}/presets", ct);
        var summary = Assert.Single(rows!).GetProperty("compatibility");
        Assert.False(summary.GetProperty("canPreview").GetBoolean());
        Assert.Equal("actionUnsupported", summary.GetProperty("previewCode").GetString());
    }

    [Fact]
    public async Task GenericPreviewErrorsKeepThePresetPreviewEnvelope()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        var create = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Malformed value",
            compatibility = new { pluginId = "test.planning", pluginVersion = "1.0.0", titleIds = CounterTitle },
            actions = new[] { new { actionId = "refill", parameters = new { value = "not-an-integer" } } },
        }, ct);
        var preset = await create.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = preset.GetProperty("id").GetString();
        var rejected = await _client.PostAsJsonAsync($"{document}/presets/{id}/preview", new { revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var rejectedBody = await rejected.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(rejectedBody.TryGetProperty("preview", out var rejectedPreview));
        Assert.True(rejectedPreview.TryGetProperty("plan", out _));
        var stale = await _client.PostAsJsonAsync($"{document}/presets/{id}/preview", new { revision = 1 }, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(staleBody.TryGetProperty("preview", out var stalePreview));
        Assert.True(stalePreview.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public async Task InvalidPresetRequestsReturnClientValidationErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        var invalid = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = " ",
            compatibility = new { pluginId = "test", pluginVersion = "1", titleIds = CounterTitle },
            actions = Array.Empty<object>(),
        }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var patch = await _client.PatchAsJsonAsync("/api/presets/missing", new { name = " " }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);

        var invalidScope = await _client.PostAsJsonAsync("/api/presets", new
        {
            name = "Bad scope",
            compatibility = new { pluginId = "test", pluginVersion = "1", titleIds = CounterTitle },
            actions = new[] { new { actionId = "refill", scopeKind = "session", parameters = new { } } },
        }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidScope.StatusCode);

        var document = await OpenAsync(ct);
        var invalidCapture = await _client.PostAsJsonAsync($"{document}/presets/capture", new
        {
            name = "Bad scope",
            actionId = "refill",
            scopeKind = "session",
        }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCapture.StatusCode);
    }

    [Fact]
    public async Task OversizedPresetEnvelopeIsRejectedBeforeJsonBinding()
    {
        var ct = TestContext.Current.CancellationToken;
        using var below = new ByteArrayContent(new byte[PresetRequestBodyLimits.CreateBytes]);
        below.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var exact = await _client.PostAsync("/api/presets", below, ct);
        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, exact.StatusCode);

        using var above = new ByteArrayContent(new byte[PresetRequestBodyLimits.CreateBytes + 1]);
        above.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var rejected = await _client.PostAsync("/api/presets", above, ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("presetPayloadTooLarge", body.GetProperty("code").GetString());
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

    [Fact]
    public async Task GenericCaptureRejectsExplicitSelectedScopeWithoutSemanticCapability()
    {
        var ct = TestContext.Current.CancellationToken;
        var document = await OpenAsync(ct);
        var captured = await _client.PostAsJsonAsync($"{document}/presets/capture", new
        {
            name = "Selected",
            actionId = "refill",
            parameters = new { value = 8 },
            scopeKind = "selected",
            selectedRowKeys = Array.Empty<string>(),
        }, ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, captured.StatusCode);
        Assert.Empty(await _client.GetFromJsonAsync<JsonElement[]>("/api/presets", ct) ?? []);
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
