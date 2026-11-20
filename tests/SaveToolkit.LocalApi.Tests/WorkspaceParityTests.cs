using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.LocalApi.Tests.Parity;
using static SaveToolkit.LocalApi.Tests.Parity.Scenarios;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>Discovery, import, workspace and download parity between the ASP.NET and in-process hosts.</summary>
public sealed class WorkspaceParityTests : IDisposable
{
    private readonly ParityHosts _hosts = new();

    [Fact]
    public Task Discovery_matches() => _hosts.AssertParityAsync(async t =>
    {
        await t.GetAsync("/api/version");
        await t.GetAsync("/api/plugins");
        await t.GetAsync("/api/titles");
        await t.GetAsync("/api/workspaces");
        await t.GetAsync("/api/presets");
        await t.GetAsync("/api/reference-packs");
    });

    [Fact]
    public Task Import_list_get_open_download_and_delete_match() => _hosts.AssertParityAsync(async t =>
    {
        var workspace = await ImportAsync(t, null, ("dump.zip", Zip(("PPSA99999/sdimg_SAVE00/data.bin", CounterPlugin.Create(7)),
            ("PPSA99999/sdimg_SAVE00/sce_sys/icon0.png", [0x89, 0x50, 0x4E, 0x47]))), ("loose 保存 (x);.dat", CounterPlugin.Create(3)));
        var url = WorkspaceUrl(workspace);
        await t.GetAsync("/api/workspaces");
        await t.GetAsync(url);
        await t.GetAsync(url.Replace("/api/workspaces/", "/API/Workspaces/", StringComparison.Ordinal) + "/");
        foreach (var title in workspace.GetProperty("titles").EnumerateArray())
            foreach (var dir in title.GetProperty("directories").EnumerateArray())
            {
                await t.GetAsync($"{url}/directories/{dir.GetProperty("id").GetString()}/icon");
                foreach (var file in dir.GetProperty("files").EnumerateArray())
                {
                    var doc = await OpenAsync(t, workspace, file.GetProperty("id").GetString(), alias: "<doc-" + file.GetProperty("path").GetString() + ">");
                    await t.GetAsync(doc);
                    await t.GetAsync(doc + "/download");
                    await t.PostJsonAsync(doc + "/edits", """{"edits":[{"valueId":"counter","value":"9"}]}""");
                    await t.GetAsync(doc + "/download");
                }
            }

        await t.GetAsync(url);
        await t.DeleteAsync(url);
        await t.DeleteAsync(url);
        await t.GetAsync(url);
        await t.GetAsync("/api/workspaces");
    });

    [Fact]
    public Task Upload_errors_match() => _hosts.AssertParityAsync(async t =>
    {
        await t.SendAsync(HttpMethod.Post, "/api/workspaces", Raw("{}", "application/json"));
        await t.SendAsync(HttpMethod.Post, "/api/workspaces");
        await t.SendAsync(HttpMethod.Post, "/api/workspaces", Raw("name=x", "application/x-www-form-urlencoded"));
        await t.SendAsync(HttpMethod.Post, "/api/workspaces", new MultipartFormDataContent { { new StringContent("only a name"), "name" } });
        await t.SendAsync(HttpMethod.Post, "/api/workspaces", Raw("--b\r\nContent-Disposition: form-data; name=\"files\"; filename=\"a\"\r\n\r\nabc", "multipart/form-data; boundary=b"));
        await t.SendAsync(HttpMethod.Post, "/api/workspaces", Raw("garbage", "multipart/form-data"));
        await t.SendAsync(HttpMethod.Post, "/api/workspaces", Raw("--b\r\nbad header\r\n\r\nabc\r\n--b--", "multipart/form-data; boundary=b"));
        var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "files", "broken.zip" } };
        await t.SendAsync(HttpMethod.Post, "/api/workspaces", form);
        var named = await t.SendAsync(HttpMethod.Post, "/api/workspaces", Raw(
            "preamble\r\n--b  \r\nContent-Disposition: form-data; name=\"name\"\r\n\r\nNamed\r\n--b\r\nContent-Disposition: form-data; name=\"f\"; filename=\"x.dat\"; filename*=UTF-8''%E4%BF%9D.dat\r\nContent-Type: application/octet-stream\r\n\r\nCNTR\r\n--b--\r\nepilogue",
            "multipart/form-data; boundary=\"b\""));
        t.Alias(named.Json.GetProperty("id").GetString(), "<ws>");
    });

    [Fact]
    public Task Routing_errors_match() => _hosts.AssertParityAsync(async t =>
    {
        await t.GetAsync("/api/unknown");
        await t.GetAsync("/api");
        await t.GetAsync("/api/workspaces/nope");
        await t.DeleteAsync("/api/workspaces/nope");
        await t.GetAsync("/api/workspaces/nope/documents/doc");
        await t.PostJsonAsync("/api/workspaces/nope/files/f/open", "{}");
        await t.SendAsync(HttpMethod.Put, "/api/workspaces");
        await t.SendAsync(HttpMethod.Post, "/api/workspaces/nope");
        await t.SendAsync(HttpMethod.Get, "/api/presets/reload");
        await t.SendAsync(HttpMethod.Head, "/api/workspaces");
        await t.SendAsync(HttpMethod.Options, "/api/plugins");
        await t.SendAsync(HttpMethod.Post, "/api/version");
        await t.GetAsync("/api//workspaces");
        await t.GetAsync("/api/workspaces/a%2Fb");
        await t.GetAsync("/api/workspaces/a%20b/documents/c%2Fd/nodes?id=x");
        await t.DeleteAsync("/api/unknown");
        await t.SendAsync(HttpMethod.Post, "/api/workspaces/nope/documents/d/nodes");
        await t.SendAsync(HttpMethod.Head, "/api/presets/reload");
        await t.SendAsync(HttpMethod.Patch, "/api/presets");
    });

    public void Dispose() => _hosts.Dispose();
}
