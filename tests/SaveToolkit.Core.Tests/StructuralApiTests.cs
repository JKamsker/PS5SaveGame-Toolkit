using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class StructuralApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
        b.ConfigureServices(s => s.AddSingleton(new PluginCatalog([new RosterPlugin()])));
    }).CreateClient();

    [Fact]
    public async Task StructuralAction_UndoRedo_AndStaleRequests_OverHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        var doc = await OpenRosterAsync(ct);
        var docUrl = doc.Url;
        Assert.Equal(0, doc.Body.GetProperty("state").GetProperty("revision").GetInt64());

        var add = await _client.PostAsJsonAsync($"{docUrl}/actions", new { actionId = "add", parameters = new { name = "cid" }, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var added = await add.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(added.GetProperty("structureChanged").GetBoolean());
        Assert.Equal(1, added.GetProperty("state").GetProperty("revision").GetInt64());

        // A client still bound to revision 0 gets a typed stale rejection.
        var stale = await _client.PostAsJsonAsync($"{docUrl}/edits", new { edits = new[] { new { valueId = "member/1/name", value = "x" } }, revision = 0 }, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.True((await stale.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("stale").GetBoolean());

        var undo = await _client.PostAsync(new Uri($"{docUrl}/undo", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        var undone = await undo.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(undone.GetProperty("structureChanged").GetBoolean());
        Assert.False(undone.GetProperty("state").GetProperty("isDirty").GetBoolean());
        Assert.True(undone.GetProperty("state").GetProperty("canRedo").GetBoolean());

        // The added member's lazy section is gone; expanding it is a typed stale error.
        var expand = await _client.GetAsync(new Uri($"{docUrl}/nodes?id=details/3", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Conflict, expand.StatusCode);
        Assert.Equal("stale", (await expand.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("code").GetString());

        var redo = await _client.PostAsync(new Uri($"{docUrl}/redo", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, redo.StatusCode);
        var expanded = await _client.GetAsync(new Uri($"{docUrl}/nodes?id=details/3", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, expanded.StatusCode);
    }

    private async Task<(string Url, JsonElement Body)> OpenRosterAsync(CancellationToken ct)
    {
        var zip = TestZips.Build(("PPSA99998/sdimg_SAVE/roster.dat", RosterPlugin.Create((1, 5, "ann"), (2, 7, "bob"))));
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(zip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "files", "dump.zip");
        var upload = await _client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, ct);
        var ws = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var wsId = ws.GetProperty("id").GetString();
        var fileId = ws.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();

        var open = await _client.PostAsJsonAsync($"/api/workspaces/{wsId}/files/{fileId}/open", new { }, ct);
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var body = await open.Content.ReadFromJsonAsync<JsonElement>(ct);
        return ($"/api/workspaces/{wsId}/documents/{body.GetProperty("id").GetString()}", body);
    }
}
