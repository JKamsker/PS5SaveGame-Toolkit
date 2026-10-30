using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class PresetTransferApiTests(IsolatedApiFactory factory) : IClassFixture<IsolatedApiFactory>
{
    private const string Transfer = "/api/presets/transfer";
    private readonly string _presetRoot = TestZips.TempDir();
    private HttpClient Client() => factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir()).UseSetting("Toolkit:PresetRoot", _presetRoot);
        builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new PlanningPlugin()])));
    }).CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FullTransferWorkflowKeepsSaveBytesDocumentRevisionDirtyAndHistoryUnchanged()
    {
        using var client = Client();
        var document = await Open(client);
        var before = await client.GetFromJsonAsync<JsonElement>(document, Ct);
        var saveBytes = await client.GetByteArrayAsync(document + "/download", Ct);
        var preview = await Preview(client);
        var summaryResponse = await client.PostAsJsonAsync(document + "/presets/transfer/compatibility", new PresetImportCommit(preview.Token), Ct);
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<JsonElement[]>(Ct);
        Assert.True(summary![0].GetProperty("compatibility").GetProperty("compatible").GetBoolean());
        Assert.Empty(await client.GetFromJsonAsync<JsonElement[]>("/api/presets", Ct) ?? []);
        var resolveResponse = await client.PostAsJsonAsync(Transfer + "/resolve", new PresetImportResolution(preview.Token,
            [new(0, "keepBoth")]), Ct);
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);
        var reviewed = (await resolveResponse.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        Assert.True(reviewed.ReadyToCommit);
        var committed = await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(reviewed.Token), Ct);
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        var imported = (await committed.Content.ReadFromJsonAsync<PresetImportResult>(Ct))!;
        var documentPresets = await client.GetFromJsonAsync<JsonElement[]>(document + "/presets", Ct);
        Assert.True(JsonElement.DeepEquals(summary![0].GetProperty("compatibility"), documentPresets![0].GetProperty("compatibility")));
        var export = await client.PostAsJsonAsync(Transfer + "/export", new PresetExportRequest(imported.Imported.Select(p => p.Id).ToArray()), Ct);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("Toolkit presets", export.Content.Headers.ContentDisposition!.FileNameStar);
        var package = PresetPackageCodec.Decode(await export.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("900719925474099312345678901", package.Presets[0].Actions[0].Parameters["huge"].GetRawText());
        var cancelled = await Preview(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Transfer + "/previews/" + cancelled.Token, Ct)).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>(document, Ct);
        Assert.True(JsonElement.DeepEquals(before.GetProperty("state"), after.GetProperty("state")));
        Assert.Equal(saveBytes, await client.GetByteArrayAsync(document + "/download", Ct));
        Assert.Single(PresetTransferFixture.Store(_presetRoot).List());
    }

    [Fact]
    public async Task InvalidFutureOversizeAndControlRequestsHaveStableTypedStatuses()
    {
        using var client = Client();
        using var malformed = new StringContent("{", Encoding.UTF8, "application/json");
        await AssertProblem(await client.PostAsync(Transfer + "/preview", malformed, Ct), 422, "invalidPresetPackage");
        using var future = new StringContent("{\"schemaVersion\":\"99\",\"provenance\":{},\"presets\":[]}", Encoding.UTF8, "application/json");
        await AssertProblem(await client.PostAsync(Transfer + "/preview", future, Ct), 422, "unsupportedPresetPackage");
        using var huge = new ByteArrayContent(new byte[PresetPackageCodec.MaxBytes + 1]);
        await AssertProblem(await client.PostAsync(Transfer + "/preview", huge, Ct), 413, "presetTransferRequestTooLarge");
        await AssertProblem(await client.PostAsJsonAsync(Transfer + "/export", new { ids = Array.Empty<string>() }, Ct), 400, "invalidPresetSelection");
        await AssertProblem(await client.PostAsJsonAsync(Transfer + "/commit", new { token = "unknown" }, Ct), 409, "stalePresetImport");
        using var invalid = new StringContent("{\"token\":null,\"choices\":[null]}", Encoding.UTF8, "application/json");
        await AssertProblem(await client.PostAsync(Transfer + "/resolve", invalid, Ct), 400, "invalidPresetTransferRequest");
    }

    [Fact]
    public async Task StalePreviewDoesNotReplaceConcurrentCrudAndStorageFailureIsRetryable()
    {
        using var client = Client();
        var preview = await Preview(client);
        var resolved = await client.PostAsJsonAsync(Transfer + "/resolve", new PresetImportResolution(preview.Token, [new(0, "keepBoth")]), Ct);
        var review = (await resolved.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        var source = PresetTransferFixture.Preset();
        var create = await client.PostAsJsonAsync("/api/presets", new { name = "Concurrent", source.Compatibility, source.Actions }, Ct);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        await AssertProblem(await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(review.Token), Ct), 409, "stalePresetImport");
        var fresh = await Preview(client);
        var next = await client.PostAsJsonAsync(Transfer + "/resolve", new PresetImportResolution(fresh.Token, [new(0, "keepBoth")]), Ct);
        var reviewed = (await next.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        Directory.CreateDirectory(Path.Combine(_presetRoot, "presets.json.tmp"));
        await AssertProblem(await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(reviewed.Token), Ct), 503, "presetStorageUnavailable");
        Assert.Single(PresetTransferFixture.Store(_presetRoot).List());
        Directory.Delete(Path.Combine(_presetRoot, "presets.json.tmp"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(reviewed.Token), Ct)).StatusCode);
        Assert.Equal(2, PresetTransferFixture.Store(_presetRoot).List().Count);
    }

    [Fact]
    public async Task IdenticalReloadInvalidatesReviewedApiImportWithoutChangingStoredRecords()
    {
        using var client = Client();
        var source = PresetTransferFixture.Preset();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/presets", new { name = "Existing", source.Compatibility, source.Actions }, Ct)).StatusCode);
        var preview = await Preview(client);
        var resolved = await client.PostAsJsonAsync(Transfer + "/resolve", new PresetImportResolution(preview.Token, [new(0, "keepBoth")]), Ct);
        var review = (await resolved.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/presets/reload", null, Ct)).StatusCode);
        await AssertProblem(await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(review.Token), Ct), 409, "stalePresetImport");
        Assert.Single(await client.GetFromJsonAsync<JsonElement[]>("/api/presets", Ct) ?? []);
    }

    [Fact]
    public async Task UnknownPluginPreviewReportsServerMismatchAndCannotMutateDocumentOrLibrary()
    {
        using var client = Client();
        var document = await Open(client);
        var before = await client.GetFromJsonAsync<JsonElement>(document, Ct);
        var source = PresetTransferFixture.Preset();
        source = source with { Compatibility = source.Compatibility with { PluginId = "unknown-for-later" } };
        using var content = new ByteArrayContent(PresetPackageCodec.Encode([source]));
        content.Headers.ContentType = new("application/json");
        var response = await client.PostAsync(Transfer + "/preview", content, Ct);
        var preview = (await response.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        var status = await client.PostAsJsonAsync(document + "/presets/transfer/compatibility", new PresetImportCommit(preview.Token), Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var summary = (await status.Content.ReadFromJsonAsync<JsonElement[]>(Ct))![0].GetProperty("compatibility");
        Assert.False(summary.GetProperty("compatible").GetBoolean());
        Assert.Equal("pluginUnavailable", summary.GetProperty("code").GetString());
        Assert.Empty(await client.GetFromJsonAsync<JsonElement[]>("/api/presets", Ct) ?? []);
        var after = await client.GetFromJsonAsync<JsonElement>(document, Ct);
        Assert.True(JsonElement.DeepEquals(before.GetProperty("state"), after.GetProperty("state")));
    }

    [Fact]
    public async Task LockedReadReturns503ForReloadAndTransferAndPreservesReviewedImport()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows exclusive file sharing.");
        using var client = Client();
        var document = await Open(client);
        var before = await client.GetFromJsonAsync<JsonElement>(document, Ct);
        var source = PresetTransferFixture.Preset();
        var created = await client.PostAsJsonAsync("/api/presets", new { name = "A", source.Compatibility, source.Actions }, Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var existing = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
        var preview = await Preview(client);
        var resolved = await client.PostAsJsonAsync(Transfer + "/resolve", new PresetImportResolution(preview.Token, [new(0, "keepBoth")]), Ct);
        var review = (await resolved.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        var path = Path.Combine(_presetRoot, "presets.json");
        var bytes = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await AssertProblem(await client.PostAsync("/api/presets/reload", null, Ct), 503, "presetStorageUnavailable");
            using var content = new ByteArrayContent(PresetPackageCodec.Encode([source]));
            await AssertProblem(await client.PostAsync(Transfer + "/preview", content, Ct), 503, "presetStorageUnavailable");
            await AssertProblem(await client.PostAsJsonAsync(Transfer + "/export", new PresetExportRequest([existing]), Ct), 503, "presetStorageUnavailable");
            Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/api/presets", Ct))!);
        }
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(review.Token), Ct)).StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>(document, Ct);
        Assert.True(JsonElement.DeepEquals(before.GetProperty("state"), after.GetProperty("state")));
    }

    [Fact]
    public async Task NullDurableArrayIs422AndDoesNotPublishAnEmptyLibrary()
    {
        using var client = Client();
        var source = PresetTransferFixture.Preset();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/presets", new { name = "A", source.Compatibility, source.Actions }, Ct)).StatusCode);
        var path = Path.Combine(_presetRoot, "presets.json");
        File.WriteAllText(path, "null");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/presets/reload", null, Ct)).StatusCode);
        Assert.Equal("null", File.ReadAllText(path));
        Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/api/presets", Ct))!);
    }

    private static async Task<PresetImportPreview> Preview(HttpClient client)
    {
        using var content = new ByteArrayContent(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]));
        content.Headers.ContentType = new("application/json");
        var response = await client.PostAsync(Transfer + "/preview", content, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
    }

    private static async Task AssertProblem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
    }

    private static async Task<string> Open(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 0 }))), "files", "synthetic.zip");
        var uploaded = await client.PostAsync("/api/workspaces", form, Ct);
        var workspace = await uploaded.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var workspaceId = workspace.GetProperty("id").GetString();
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var opened = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/files/{fileId}/open", new { pluginId = "test.planning" }, Ct);
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return $"/api/workspaces/{workspaceId}/documents/{document.GetProperty("id").GetString()}";
    }
}
