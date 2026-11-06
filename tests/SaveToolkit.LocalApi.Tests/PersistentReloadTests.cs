using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Core.Tests.References;
using SaveToolkit.LocalApi.Tests.Parity;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

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
    public async Task A_library_this_host_cannot_read_is_never_overwritten_by_its_stale_view()
    {
        var roots = new Roots();
        using var tabA = roots.Host();
        using var tabB = roots.Host();
        Assert.Equal(200, (await CreatePresetAsync(tabA, "From A")).Status);
        var path = Path.Combine(roots.Presets, "presets.json");
        // Another tab running a newer version of the site saved a record this version cannot stage.
        const string Record = """{"schemaVersion":"2","id":"newer","name":"From newer tab","compatibility":{"pluginId":"p","pluginVersion":"1","titleIds":["T"]},"actions":[{"actionId":"x","parameters":{}}],"createdAt":"2026-01-01T00:00:00Z","updatedAt":"2026-01-01T00:00:00Z"}""";
        var newer = File.ReadAllText(path).TrimEnd().TrimEnd(']') + "," + Record + "]";
        await File.WriteAllTextAsync(path, newer, Ct);
        Assert.Throws<InvalidDataException>(tabB.ReloadPersistentStores);

        var created = await CreatePresetAsync(tabB, "From B");
        Assert.Equal(409, created.Status);
        Assert.Contains("presetLibraryReloadRequired", Encoding.UTF8.GetString(created.Body), StringComparison.Ordinal);
        Assert.Equal(409, (await SendAsync(tabA, "PATCH", "/api/presets/" + await FirstIdAsync(tabA), """{"name":"Renamed"}""")).Status);
        Assert.Equal(409, (await SendAsync(tabA, "DELETE", "/api/presets/" + await FirstIdAsync(tabA))).Status);
        Assert.Equal(newer, await File.ReadAllTextAsync(path, Ct));
    }

    [Fact]
    public async Task A_stale_view_is_refused_until_reloaded()
    {
        var roots = new Roots();
        using var tabA = roots.Host();
        using var tabB = roots.Host();
        Assert.Equal(200, (await CreatePresetAsync(tabA, "From A")).Status);

        Assert.Equal(409, (await CreatePresetAsync(tabB, "From B")).Status);
        tabB.ReloadPersistentStores();
        Assert.Equal(200, (await CreatePresetAsync(tabB, "From B")).Status);
        using var fresh = roots.Host();
        var names = await TextAsync(fresh, "GET", "/api/presets");
        Assert.Contains("From A", names, StringComparison.Ordinal);
        Assert.Contains("From B", names, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reviewed_import_survives_a_reload_after_an_unrelated_change()
    {
        var roots = new Roots();
        using var tabA = roots.Host();
        using var tabB = roots.Host();
        Assert.Equal(200, (await CreatePresetAsync(tabA, "X")).Status);
        var package = await SendAsync(tabA, "POST", "/api/presets/transfer/export", $$"""{"ids":["{{await FirstIdAsync(tabA)}}"]}""");
        var preview = await SendAsync(tabA, "POST", "/api/presets/transfer/preview", Encoding.UTF8.GetString(package.Body));
        var resolved = await SendAsync(tabA, "POST", "/api/presets/transfer/resolve",
            $$"""{"token":"{{Token(preview)}}","choices":[{"index":0,"decision":"keepBoth"}]}""");

        // Tab B imports a reference pack; tab A then reloads every durable store, as the worker does.
        var (contentType, body) = Multipart("pack", "user items.zip", PackBuilder.WithItems().Zip());
        Assert.Equal(200, (await tabB.SendAsync(new LocalRequest("POST", "/api/reference-packs", [new("Content-Type", contentType)], body), Ct)).Status);
        tabA.ReloadPersistentStores();
        var commit = await SendAsync(tabA, "POST", "/api/presets/transfer/commit", $$"""{"token":"{{Token(resolved)}}"}""");
        Assert.Equal(200, commit.Status);
        Assert.Contains("user.items", await TextAsync(tabA, "GET", "/api/reference-packs"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_library_unreadable_at_startup_fails_only_preset_requests_until_repaired()
    {
        var roots = new Roots();
        Directory.CreateDirectory(roots.Presets);
        var path = Path.Combine(roots.Presets, "presets.json");
        const string Newer = """[{"schemaVersion":"2","id":"newer","name":"From a newer version"}]""";
        await File.WriteAllTextAsync(path, Newer, Ct);

        using var host = roots.Host();
        host.ReloadPersistentStores();
        Assert.Equal(200, (await SendAsync(host, "GET", "/api/reference-packs")).Status);
        var listed = await SendAsync(host, "GET", "/api/presets");
        Assert.Equal(422, listed.Status);
        using (var problem = JsonDocument.Parse(listed.Body))
            Assert.Equal("Invalid data: Preset schema '2' is not supported.", problem.RootElement.GetProperty("error").GetString());
        Assert.Equal(422, (await CreatePresetAsync(host, "Lost")).Status);
        Assert.Equal(Newer, await File.ReadAllTextAsync(path, Ct));

        File.Delete(path);
        Assert.Equal("[]", await TextAsync(host, "GET", "/api/presets"));
        Assert.Equal(200, (await CreatePresetAsync(host, "Works")).Status);
    }

    private static string Token(LocalResponse response)
    {
        Assert.Equal(200, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<string> FirstIdAsync(LocalApiHost host)
    {
        using var list = JsonDocument.Parse(await TextAsync(host, "GET", "/api/presets"));
        return list.RootElement[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Unexpected_failures_use_the_hosts_description_without_changing_the_status()
    {
        LocalApiHost FailingHost(Func<Exception, string>? describe) => new(new LocalApiOptions
        {
            DataRoot = ParityHosts.TempDir(), SweepInterval = null, DescribeUnexpectedError = describe,
            Catalog = new PluginCatalog([new ExhaustedPlugin()]),
        });
        using var shared = FailingHost(null);
        using var browser = FailingHost(exception => $"described {exception.GetType().Name}");

        var expected = await OpenFailingDocumentAsync(shared);
        var actual = await OpenFailingDocumentAsync(browser);
        Assert.Equal(500, expected.Status);
        Assert.Equal(500, actual.Status);
        Assert.Equal("Unexpected server error. See the server log for details.", Detail(expected));
        Assert.Equal("described InsufficientMemoryException", Detail(actual));
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

    /// <summary>Claims every file, then fails to open it the way an exhausted WebAssembly heap does.</summary>
    private sealed class ExhaustedPlugin : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.exhausted", "Exhausted heap", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Title);
        public ISaveDocument Open(ISaveFile file) => throw new InsufficientMemoryException();
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
