using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Core.Tests.References;
using SaveToolkit.LocalApi.Tests.Parity;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>
/// Two hosts over the same preset and pack roots model two browser tabs over one IndexedDB: the
/// worker refreshes the files, then <see cref="LocalApiHost.ReloadPersistentStores"/> must make the
/// stores forget their cached view, or the next write would put the stale view back on disk.
/// </summary>
public sealed class PersistentReloadTests
{
    private const string Preset = """{"name":"NAME","compatibility":{"pluginId":"test.items","pluginVersion":"1.0.0","titleIds":["T"]},"actions":[{"actionId":"max","parameters":{}}]}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reloaded_presets_keep_the_other_writers_records()
    {
        var roots = new Roots();
        using var tabA = roots.Host();
        using var tabB = roots.Host();
        Assert.Equal(200, (await CreatePresetAsync(tabA, "From A")).Status);

        Assert.DoesNotContain("From A", await TextAsync(tabB, "GET", "/api/presets"), StringComparison.Ordinal);
        tabB.ReloadPersistentStores();
        Assert.Contains("From A", await TextAsync(tabB, "GET", "/api/presets"), StringComparison.Ordinal);
        Assert.Equal(200, (await CreatePresetAsync(tabB, "From B")).Status);

        using var fresh = roots.Host();
        var names = await TextAsync(fresh, "GET", "/api/presets");
        Assert.Contains("From A", names, StringComparison.Ordinal);
        Assert.Contains("From B", names, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reloaded_user_packs_follow_imports_and_removals_elsewhere()
    {
        var roots = new Roots();
        using var tabA = roots.Host();
        using var tabB = roots.Host();
        Assert.DoesNotContain("user.items", await TextAsync(tabB, "GET", "/api/reference-packs"), StringComparison.Ordinal);

        var (contentType, body) = Multipart("pack", "user items.zip", PackBuilder.WithItems().Zip());
        Assert.Equal(200, (await tabA.SendAsync(new LocalRequest("POST", "/api/reference-packs", [new("Content-Type", contentType)], body), Ct)).Status);
        tabB.ReloadPersistentStores();
        Assert.Contains("user.items", await TextAsync(tabB, "GET", "/api/reference-packs"), StringComparison.Ordinal);

        Assert.Equal(204, (await SendAsync(tabA, "DELETE", "/api/reference-packs/user.items")).Status);
        tabB.ReloadPersistentStores();
        Assert.DoesNotContain("user.items", await TextAsync(tabB, "GET", "/api/reference-packs"), StringComparison.Ordinal);
        Assert.Equal(404, (await SendAsync(tabB, "DELETE", "/api/reference-packs/user.items")).Status);
    }

    [Fact]
    public async Task A_vanished_preset_root_reloads_as_empty_and_is_recreated_on_write()
    {
        var roots = new Roots();
        using var host = roots.Host();
        Assert.Equal(200, (await CreatePresetAsync(host, "Gone")).Status);
        Directory.Delete(roots.Presets, recursive: true);

        host.ReloadPersistentStores();
        Assert.Equal("[]", await TextAsync(host, "GET", "/api/presets"));
        Assert.Equal(200, (await CreatePresetAsync(host, "Back")).Status);
        Assert.True(File.Exists(Path.Combine(roots.Presets, "presets.json")));
    }

    [Fact]
    public async Task A_malformed_library_keeps_the_last_good_view_and_reports_it()
    {
        var roots = new Roots();
        using var host = roots.Host();
        Assert.Equal(200, (await CreatePresetAsync(host, "Kept")).Status);
        await File.WriteAllTextAsync(Path.Combine(roots.Presets, "presets.json"), "{broken", Ct);

        Assert.Throws<InvalidDataException>(host.ReloadPersistentStores);
        Assert.Contains("Kept", await TextAsync(host, "GET", "/api/presets"), StringComparison.Ordinal);
        host.Dispose();
        Assert.Throws<ObjectDisposedException>(host.ReloadPersistentStores);
    }

    [Fact]
    public async Task Unexpected_failures_use_the_hosts_description_without_changing_the_status()
    {
        LocalApiHost FailingHost(Func<Exception, string>? describe) => new(new LocalApiOptions
        {
            DataRoot = ParityHosts.TempDir(), SweepInterval = null, DescribeUnexpectedError = describe,
            Catalog = new PluginCatalog([new FuzzPlugin { ThrowDuring = "view", Failure = () => new OutOfMemoryException() }]),
        });
        using var shared = FailingHost(null);
        using var browser = FailingHost(exception => $"described {exception.GetType().Name}");

        var expected = await OpenFailingDocumentAsync(shared);
        var actual = await OpenFailingDocumentAsync(browser);
        Assert.Equal(500, expected.Status);
        Assert.Equal(500, actual.Status);
        Assert.Equal("Unexpected server error. See the server log for details.", Detail(expected));
        Assert.Equal("described OutOfMemoryException", Detail(actual));
    }

    private static async Task<LocalResponse> OpenFailingDocumentAsync(LocalApiHost host)
    {
        var (contentType, body) = Multipart("files", "save.dat", [1, 2, 3]);
        var imported = await host.SendAsync(new LocalRequest("POST", "/api/workspaces", [new("Content-Type", contentType)], body), Ct);
        Assert.Equal(200, imported.Status);
        using var workspace = JsonDocument.Parse(imported.Body);
        var id = workspace.RootElement.GetProperty("id").GetString();
        var file = workspace.RootElement.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        return await SendAsync(host, "POST", $"/api/workspaces/{id}/files/{file}/open", "{}");
    }

    private static string Detail(LocalResponse response)
    {
        using var json = JsonDocument.Parse(response.Body);
        return json.RootElement.GetProperty("detail").GetString()!;
    }

    private static Task<LocalResponse> CreatePresetAsync(LocalApiHost host, string name) =>
        SendAsync(host, "POST", "/api/presets", Preset.Replace("NAME", name, StringComparison.Ordinal));

    private static async Task<string> TextAsync(LocalApiHost host, string method, string path)
    {
        var response = await SendAsync(host, method, path);
        Assert.Equal(200, response.Status);
        return Encoding.UTF8.GetString(response.Body);
    }

    private static Task<LocalResponse> SendAsync(LocalApiHost host, string method, string path, string? json = null) =>
        host.SendAsync(new LocalRequest(method, path, json is null ? [] : [new("Content-Type", "application/json")],
            json is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(json)), Ct);

    private static (string ContentType, byte[] Body) Multipart(string field, string fileName, byte[] content)
    {
        var body = new MemoryStream();
        body.Write(Encoding.UTF8.GetBytes($"--x\r\nContent-Disposition: form-data; name=\"{field}\"; filename=\"{fileName}\"\r\n\r\n"));
        body.Write(content);
        body.Write("\r\n--x--\r\n"u8);
        return ("multipart/form-data; boundary=x", body.ToArray());
    }

    /// <summary>Shared durable roots with a private workspace root per host, like tabs of one site.</summary>
    private sealed class Roots
    {
        private readonly string _durable = ParityHosts.TempDir();

        public string Presets => Path.Combine(_durable, "presets");

        public LocalApiHost Host() => new(new LocalApiOptions
        {
            DataRoot = ParityHosts.TempDir(), PresetRoot = Presets, PackRoot = Path.Combine(_durable, "reference-packs"),
            SweepInterval = null, Catalog = new PluginCatalog([new ItemsPlugin()]),
        });
    }
}
