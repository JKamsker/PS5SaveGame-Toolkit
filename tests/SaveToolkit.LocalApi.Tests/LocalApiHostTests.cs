using System.Net;
using System.Text;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.LocalApi.Tests.Parity;
using SaveToolkit.Testing;

namespace SaveToolkit.LocalApi.Tests;

/// <summary>In-process host behaviour that has no ASP.NET counterpart to compare with directly.</summary>
public sealed class LocalApiHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Idle_sweep_removes_clean_workspaces_and_retains_dirty_ones()
    {
        using var host = Host(new LocalApiOptions { DataRoot = ParityHosts.TempDir(), WorkspaceIdleTimeout = TimeSpan.Zero, SweepInterval = null });
        var clean = await ImportAsync(host);
        var dirty = await ImportAsync(host);
        var document = await OpenAsync(host, dirty);
        Assert.Equal(200, (await SendAsync(host, "POST", document + "/edits", """{"edits":[{"valueId":"counter","value":"9"}]}""")).Status);
        await Task.Delay(50, Ct);

        Assert.Equal(1, host.SweepIdleWorkspaces());
        Assert.Equal(404, (await SendAsync(host, "GET", clean)).Status);
        Assert.Equal(200, (await SendAsync(host, "GET", dirty)).Status);
    }

    [Fact]
    public async Task Background_sweep_runs_on_the_configured_clock()
    {
        var clock = new ManualClock();
        using var host = Host(new LocalApiOptions
        {
            DataRoot = ParityHosts.TempDir(), WorkspaceIdleTimeout = TimeSpan.Zero, SweepInterval = TimeSpan.FromMinutes(10), TimeProvider = clock,
        });
        var workspace = await ImportAsync(host);
        await Task.Delay(50, Ct);
        Assert.Equal(TimeSpan.FromMinutes(10), clock.Period);

        clock.Fire();
        for (var attempt = 0; attempt < 100 && (await SendAsync(host, "GET", workspace)).Status != 404; attempt++)
            await Task.Delay(10, Ct);
        Assert.Equal(404, (await SendAsync(host, "GET", workspace)).Status);
    }

    [Fact]
    public async Task Cancelled_requests_are_499_without_a_body()
    {
        using var host = Host();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var (contentType, body) = Upload("a.dat", CounterPlugin.Create(1));
        var response = await host.SendAsync(new LocalRequest("POST", "/api/workspaces", [new("Content-Type", contentType)], body), cancellation.Token);
        Assert.Equal(499, response.Status);
        Assert.Empty(response.Body);
        Assert.Equal(200, (await SendAsync(host, "GET", "/api/workspaces")).Status);
    }

    [Fact]
    public async Task Reference_pack_uploads_over_the_request_limit_are_413()
    {
        using var host = Host();
        var body = new byte[checked((int)Protocol.ApiLimits.MaxReferencePackBody + 1)];
        var response = await host.SendAsync(new LocalRequest("POST", "/api/reference-packs",
            [new("Content-Type", "multipart/form-data; boundary=b")], body), Ct);
        Assert.Equal(413, response.Status);
        Assert.Contains("Request body too large", Encoding.UTF8.GetString(response.Body), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_api_paths_have_no_problem_body_and_disposal_is_final()
    {
        var host = Host();
        var response = await host.SendAsync(new LocalRequest("GET", "/index.html", [], ReadOnlyMemory<byte>.Empty), Ct);
        Assert.Equal((int)HttpStatusCode.NotFound, response.Status);
        Assert.Empty(response.Body);
        host.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => SendAsync(host, "GET", "/api/version"));
    }

    [Fact]
    public async Task Version_and_storage_roots_follow_the_options()
    {
        var root = ParityHosts.TempDir();
        using var host = Host(new LocalApiOptions { DataRoot = root, Version = "1.2.3-test", SweepInterval = null });
        var version = await SendAsync(host, "GET", "/api/version");
        Assert.Equal("""{"version":"1.2.3-test"}""", Encoding.UTF8.GetString(version.Body));
        await ImportAsync(host);
        var created = await SendAsync(host, "POST", "/api/presets",
            """{"name":"p","compatibility":{"pluginId":"test.counter","pluginVersion":"1.0.0","titleIds":["T"]},"actions":[{"actionId":"max","parameters":{}}]}""");
        Assert.Equal(200, created.Status);
        Assert.True(Directory.Exists(Path.Combine(root, "workspaces")));
        Assert.True(Directory.Exists(Path.Combine(root, "presets")));
    }

    private static LocalApiHost Host(LocalApiOptions? options = null)
    {
        options ??= new LocalApiOptions { DataRoot = ParityHosts.TempDir(), SweepInterval = null };
        return new LocalApiHost(new LocalApiOptions
        {
            DataRoot = options.DataRoot, Version = options.Version, WorkspaceIdleTimeout = options.WorkspaceIdleTimeout,
            SweepInterval = options.SweepInterval, TimeProvider = options.TimeProvider, Catalog = new PluginCatalog([new CounterPlugin()]),
        });
    }

    private static async Task<string> ImportAsync(LocalApiHost host)
    {
        var (contentType, body) = Upload("save.dat", CounterPlugin.Create(5));
        var response = await host.SendAsync(new LocalRequest("POST", "/api/workspaces", [new("Content-Type", contentType)], body), Ct);
        Assert.Equal(200, response.Status);
        using var json = System.Text.Json.JsonDocument.Parse(response.Body);
        return "/api/workspaces/" + json.RootElement.GetProperty("id").GetString();
    }

    private static async Task<string> OpenAsync(LocalApiHost host, string workspace)
    {
        using var json = System.Text.Json.JsonDocument.Parse((await SendAsync(host, "GET", workspace)).Body);
        var file = json.RootElement.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        var opened = await SendAsync(host, "POST", $"{workspace}/files/{file}/open", "{}");
        using var document = System.Text.Json.JsonDocument.Parse(opened.Body);
        return $"{workspace}/documents/{document.RootElement.GetProperty("id").GetString()}";
    }

    private static Task<LocalResponse> SendAsync(LocalApiHost host, string method, string path, string? json = null) =>
        host.SendAsync(new LocalRequest(method, path, json is null ? [] : [new("Content-Type", "application/json")],
            json is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(json)), Ct);

    private static (string ContentType, byte[] Body) Upload(string fileName, byte[] content)
    {
        var body = new MemoryStream();
        body.Write(Encoding.UTF8.GetBytes($"--x\r\nContent-Disposition: form-data; name=\"files\"; filename=\"{fileName}\"\r\n\r\n"));
        body.Write(content);
        body.Write("\r\n--x--\r\n"u8);
        return ("multipart/form-data; boundary=x", body.ToArray());
    }

    /// <summary>A clock whose single periodic timer fires only when the test says so.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;

        public TimeSpan Period { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            Period = period;
            return new NoopTimer();
        }

        public void Fire() => _callback?.Invoke(_state);

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
