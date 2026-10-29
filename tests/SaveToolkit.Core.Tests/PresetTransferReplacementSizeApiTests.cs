using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Presets;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class PresetTransferReplacementSizeApiTests(IsolatedApiFactory factory, ITestOutputHelper output)
    : IClassFixture<IsolatedApiFactory>
{
    private const string Transfer = "/api/presets/transfer";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExplicitReplacementsCanFitWhenIncomingDurableRepresentationDoesNot()
    {
        var fixture = PresetReplacementSizeFixture.Create();
        output.WriteLine($"Actual bytes: compact={fixture.Package.Length}, incoming durable={fixture.IncomingBytes}, replacement durable={fixture.ResultBytes}; text length={fixture.TextLength}.");
        var root = TestZips.TempDir();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Toolkit:PresetRoot", root);
            builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new PlanningPlugin()])));
        });
        using var client = host.CreateClient();
        var store = host.Services.GetRequiredService<PresetStore>();
        var document = await Open(client);
        var before = await client.GetFromJsonAsync<JsonElement>(document, Ct);
        var save = await client.GetByteArrayAsync(document + "/download", Ct);
        var path = Path.Combine(root, "presets.json");
        var emptyIdentity = store.Snapshot().Identity;

        // Count 200 fits, but Keep both cannot pass the resulting durable-byte limit.
        var emptyPreview = await Preview(client, fixture.Package);
        Assert.False(emptyPreview.ReadyToCommit);
        await Problem(await Resolve(client, emptyPreview, fixture, replace: false), 422, "invalidPresetLibrary");
        await Problem(await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(emptyPreview.Token), Ct),
            409, "presetImportNotReviewed");
        Assert.Empty(store.List());
        Assert.Equal(emptyIdentity, store.Snapshot().Identity);
        Assert.False(File.Exists(path));
        await UnchangedDocument();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Transfer + "/previews/" + emptyPreview.Token, Ct)).StatusCode);

        store.ImportLibrary(emptyIdentity, fixture.Existing);
        var baseline = await File.ReadAllBytesAsync(path, Ct);
        var identity = store.Snapshot().Identity;
        var fingerprints = store.List().ToDictionary(p => p.Id, PresetStore.PayloadFingerprint);
        var preview = await Preview(client, fixture.Package);
        Assert.Equal(PresetReplacementSizeFixture.Count, preview.Items.Count);
        foreach (var item in preview.Items)
            Assert.Equal(fixture.Existing[item.Index].Id, Assert.Single(item.Conflicts).Id);
        // Keeping the baseline as well would violate count, and cannot produce a confirmation token.
        await Problem(await Resolve(client, preview, fixture, replace: false), 422, "presetLibraryFull");
        await UnchangedLibrary();
        await UnchangedDocument();

        using var resolution = await Resolve(client, preview, fixture, replace: true);
        Assert.Equal(HttpStatusCode.OK, resolution.StatusCode);
        var review = (await resolution.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        Assert.True(review.ReadyToCommit);
        foreach (var item in review.Items)
        {
            Assert.Equal(fixture.Incoming[item.Index].Name, item.ResultingName);
            Assert.Equal(fixture.Existing[item.Index].Name, item.ReplacesName);
        }
        await UnchangedLibrary();

        var temporaryPath = Path.Combine(root, "presets.json.tmp");
        Directory.CreateDirectory(temporaryPath);
        await Problem(await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(review.Token), Ct),
            503, "presetStorageUnavailable");
        await UnchangedLibrary();
        await UnchangedDocument();
        Directory.Delete(temporaryPath);
        using var committed = await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(review.Token), Ct);
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        var reopened = new PresetStore(new() { StorageRoot = root }).List();
        Assert.Equal(fixture.Existing.Select(p => p.Id).Order(), reopened.Select(p => p.Id).Order());
        Assert.Equal(fingerprints[fixture.Existing[^1].Id], PresetStore.PayloadFingerprint(reopened.Single(p => p.Id == fixture.Existing[^1].Id)));
        foreach (var expected in fixture.Incoming.Select((preset, index) => preset with { Id = fixture.Existing[index].Id }))
        {
            var actual = reopened.Single(p => p.Id == expected.Id);
            Assert.Equal(PresetStore.PayloadFingerprint(expected), PresetStore.PayloadFingerprint(actual));
            Assert.Equal(new string('x', fixture.TextLength), actual.Actions[0].Parameters["text"].GetString());
            Assert.Equal("900719925474099312345678901", actual.Actions[0].Parameters["huge"].GetRawText());
            Assert.Equal("1.2300e+120", actual.Actions[0].Parameters["lexeme"].GetRawText());
        }
        Assert.Equal(fixture.ResultBytes, new FileInfo(path).Length);
        Assert.InRange(new FileInfo(path).Length, 1, PresetPackageCodec.MaxBytes);
        await UnchangedDocument();

        // Commit's authoritative Stage also rejects an oversized graph, independent of prior preview validation.
        var committedBytes = await File.ReadAllBytesAsync(path, Ct);
        var committedIdentity = store.Snapshot().Identity;
        var error = Assert.Throws<PresetTransferException>(() => store.ImportLibrary(committedIdentity, fixture.Incoming));
        Assert.Equal("invalidPresetLibrary", error.Code);
        Assert.Equal(committedBytes, await File.ReadAllBytesAsync(path, Ct));
        Assert.Equal(committedIdentity, store.Snapshot().Identity);
        Assert.Equal(fixture.Existing.Select(p => p.Id).Order(), store.List().Select(p => p.Id).Order());
        await UnchangedDocument();

        async Task UnchangedLibrary()
        {
            Assert.Equal(baseline, await File.ReadAllBytesAsync(path, Ct));
            Assert.Equal(identity, store.Snapshot().Identity);
            Assert.Equal(fingerprints, store.List().ToDictionary(p => p.Id, PresetStore.PayloadFingerprint));
        }

        async Task UnchangedDocument()
        {
            var after = await client.GetFromJsonAsync<JsonElement>(document, Ct);
            Assert.True(JsonElement.DeepEquals(before.GetProperty("state"), after.GetProperty("state")));
            Assert.Equal(save, await client.GetByteArrayAsync(document + "/download", Ct));
        }
    }

    private static Task<HttpResponseMessage> Resolve(HttpClient client, PresetImportPreview preview,
        PresetReplacementSizeFixture fixture, bool replace) => client.PostAsJsonAsync(Transfer + "/resolve",
        new PresetImportResolution(preview.Token, preview.Items.Select(item => new PresetImportChoice(item.Index,
            replace ? "replace" : "keepBoth", replace ? fixture.Existing[item.Index].Id : null)).ToArray()), Ct);

    private static async Task<PresetImportPreview> Preview(HttpClient client, byte[] package)
    {
        using var content = new ByteArrayContent(package);
        content.Headers.ContentType = new("application/json");
        using var response = await client.PostAsync(Transfer + "/preview", content, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
    }

    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        using (response)
        {
            Assert.Equal(status, (int)response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(code, body.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("error").GetString()));
        }
    }

    private static async Task<string> Open(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 0 }))), "files", "synthetic.zip");
        using var upload = await client.PostAsync("/api/workspaces", form, Ct);
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var workspaceId = workspace.GetProperty("id").GetString();
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        using var opened = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/files/{fileId}/open", new { pluginId = "test.planning" }, Ct);
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return $"/api/workspaces/{workspaceId}/documents/{document.GetProperty("id").GetString()}";
    }
}
