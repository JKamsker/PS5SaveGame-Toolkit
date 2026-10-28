using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class DocumentGroupApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([new GroupPlugin()])));
    }).CreateClient();

    private async Task<(string Workspace, string Character, string Profile)> Upload(CancellationToken ct)
    {
        var zip = TestZips.Build(("PPSA99990/sdimg_SAVE/character.dat", GroupPlugin.Create("C", 10, 7, 7)),
            ("PPSA99990/sdimg_SAVE/profile.dat", GroupPlugin.Create("P", 20, 9)));
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(zip);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(content, "files", "groups.zip");
        var response = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, ct);
        response.EnsureSuccessStatusCode();
        var workspace = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var files = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files").EnumerateArray().ToArray();
        return ($"/api/workspaces/{workspace.GetProperty("id").GetString()}",
            files.Single(file => file.GetProperty("path").GetString() == "character.dat").GetProperty("id").GetString()!,
            files.Single(file => file.GetProperty("path").GetString() == "profile.dat").GetProperty("id").GetString()!);
    }

    private async Task<string> Proposal(string workspace, string character, CancellationToken ct)
    {
        var offers = await _client.GetFromJsonAsync<JsonElement>($"{workspace}/files/{character}/group-proposals?pluginId={GroupPlugin.PluginId}", ct);
        Assert.Equal(2, offers[0].GetProperty("members").GetArrayLength());
        Assert.Equal(0, offers[0].GetProperty("errors").GetArrayLength());
        return offers[0].GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Group_AtomicEdit_Undo_Metadata_OwnershipAndExport_OverHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        var (workspace, character, profile) = await Upload(ct);
        var token = await Proposal(workspace, character, ct);
        var opened = await _client.PostAsJsonAsync($"{workspace}/document-groups", new { proposalToken = token }, ct);
        opened.EnsureSuccessStatusCode();
        var group = await opened.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(group.GetProperty("plugin").GetProperty("supportsGroups").GetBoolean());
        Assert.Equal(2, group.GetProperty("groupMembers").GetArrayLength());
        var document = $"{workspace}/documents/{group.GetProperty("id").GetString()}";
        var overview = await _client.GetFromJsonAsync<JsonElement>(workspace, ct);
        Assert.Equal(2, overview.GetProperty("documents")[0].GetProperty("groupMembers").GetArrayLength());
        Assert.All(overview.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files").EnumerateArray(),
            file => Assert.Equal(group.GetProperty("id").GetString(), file.GetProperty("openDocumentId").GetString()));
        var conflict = await _client.PostAsJsonAsync($"{workspace}/files/{profile}/open", new { }, ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.GetAsync(new Uri(document + "/download", UriKind.Relative), ct)).StatusCode);

        var edit = await _client.PostAsJsonAsync(document + "/edits", new
        {
            revision = 0,
            edits = new[] { new { valueId = "character/balance", value = "11" }, new { valueId = "profile/balance", value = "19" } },
        }, ct);
        edit.EnsureSuccessStatusCode();
        var changed = await edit.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(changed.GetProperty("structureChanged").GetBoolean());
        var stale = await _client.GetAsync(new Uri(document + "/collections/profile?revision=0", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var plan = await _client.PostAsJsonAsync(workspace + "/export/plan", new { scope = "ChangedFiles", mirrorBackups = true }, ct);
        plan.EnsureSuccessStatusCode();
        Assert.Equal(2, (await plan.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("changedCount").GetInt32());
        var exported = await _client.PostAsJsonAsync(workspace + "/export", new { scope = "ChangedFiles" }, ct);
        exported.EnsureSuccessStatusCode();
        using var zip = new System.IO.Compression.ZipArchive(await exported.Content.ReadAsStreamAsync(ct));
        Assert.Equal(2, zip.Entries.Count);
        var undo = await _client.PostAsync(new Uri(document + "/undo", UriKind.Relative), null, ct);
        Assert.False((await undo.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetProperty("isDirty").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(new Uri(document, UriKind.Relative), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"{workspace}/files/{profile}/open", new { }, ct)).StatusCode);
    }

    [Fact]
    public async Task Confirmation_ReleasesOnlyCleanMember_AndReportsDirtyConflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        var (workspace, character, _) = await Upload(ct);
        var single = await _client.PostAsJsonAsync($"{workspace}/files/{character}/open", new { }, ct);
        var documentId = (await single.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString();
        var token = await Proposal(workspace, character, ct);
        var edits = await _client.PostAsJsonAsync($"{workspace}/documents/{documentId}/edits", new
        { edits = new[] { new { valueId = "balance", value = "11" } } }, ct);
        edits.EnsureSuccessStatusCode();
        var rejected = await _client.PostAsJsonAsync(workspace + "/document-groups",
            new { proposalToken = token, releaseCleanDocumentId = documentId }, ct);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("dirty", await rejected.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        var undo = await _client.PostAsync(new Uri($"{workspace}/documents/{documentId}/undo", UriKind.Relative), null, ct);
        undo.EnsureSuccessStatusCode();
        var group = await _client.PostAsJsonAsync(workspace + "/document-groups",
            new { proposalToken = token, releaseCleanDocumentId = documentId }, ct);
        group.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(new Uri($"{workspace}/documents/{documentId}", UriKind.Relative), ct)).StatusCode);
    }
}
