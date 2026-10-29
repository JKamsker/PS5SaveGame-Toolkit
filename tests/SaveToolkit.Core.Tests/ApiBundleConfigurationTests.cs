using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using SaveToolkit.Core.Tests.References;

namespace SaveToolkit.Core.Tests;

/// <summary>Run against an extracted self-contained bundle via SAVETOOLKIT_TEST_BUNDLE.</summary>
public sealed class ApiBundleConfigurationTests
{
    private static readonly string[] Providers = ["appsettings", "environment", "commandLine"];

    [Theory]
    [InlineData("appsettings")]
    [InlineData("environment")]
    [InlineData("commandLine")]
    public async Task PackagedApphost_UsesUrlsAndStorageOverrides(string source)
    {
        string? bundle = Environment.GetEnvironmentVariable("SAVETOOLKIT_TEST_BUNDLE");
        Assert.SkipWhen(string.IsNullOrEmpty(bundle), "Set SAVETOOLKIT_TEST_BUNDLE to an extracted native bundle to verify packaging overrides.");
        string executable = Path.Combine(bundle, OperatingSystem.IsWindows() ? "SaveToolkit.Api.exe" : "SaveToolkit.Api");
        Assert.True(File.Exists(executable), $"Missing packaged apphost: {executable}");
        string root = Path.Combine(TestZips.TempDir(), "bundle config");
        Directory.CreateDirectory(root);
        string Data(string provider) => Path.Combine(root, provider, "workspaces");
        string Packs(string provider) => Path.Combine(root, provider, "reference-packs");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var url = new Uri($"http://127.0.0.1:{port}");
        var start = CreateStartInfo(executable, bundle, root, source, url);
        await using var server = new BundleServer(start);
        using var client = new HttpClient { BaseAddress = url, Timeout = TimeSpan.FromSeconds(5) };
        await server.WaitUntilReadyAsync(client);
        var ct = TestContext.Current.CancellationToken;
        string html = await client.GetStringAsync(new Uri("/", UriKind.Relative), ct);
        Assert.Contains("<div id=\"root\">", html, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Data(source)), "Workspace storage must use the winning configuration provider.");
        using var workspaceForm = new MultipartFormDataContent();
        workspaceForm.Add(new ByteArrayContent(TestZips.Build(("PPSA00001/sdimg_SAVE/data.bin", [1, 2, 3]))), "files", "dump.zip");
        using var imported = await client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), workspaceForm, ct);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var workspace = await imported.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(Directory.Exists(Path.Combine(Data(source), workspace.GetProperty("id").GetString()!)));
        using var packForm = new MultipartFormDataContent();
        packForm.Add(new ByteArrayContent(PackBuilder.WithItems().Zip()), "file", "pack.zip");
        using var pack = await client.PostAsync(new Uri("/api/reference-packs", UriKind.Relative), packForm, ct);
        Assert.Equal(HttpStatusCode.OK, pack.StatusCode);
        Assert.True(File.Exists(Path.Combine(Packs(source), "pack-user.items.zip")));
        foreach (string other in Providers.Where(value => value != source))
        {
            Assert.False(Directory.Exists(Data(other)));
            Assert.False(Directory.Exists(Packs(other)));
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable, string bundle, string root, string source, Uri url)
    {
        string Data(string provider) => Path.Combine(root, provider, "workspaces");
        string Packs(string provider) => Path.Combine(root, provider, "reference-packs");
        File.WriteAllText(Path.Combine(root, "appsettings.json"), JsonSerializer.Serialize(new
        {
            Urls = "http://127.0.0.1:1",
            Toolkit = new { DataRoot = Data("appsettings"), PackRoot = Packs("appsettings"), Locales = "en" },
        }));
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { "--urls", url.ToString(), "--contentRoot", root,
            "--webroot", Path.Combine(bundle, "wwwroot") }) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["URLS"] = "http://127.0.0.1:2";
        start.Environment["Toolkit__DataRoot"] = source == "appsettings" ? "" : Data("environment");
        start.Environment["Toolkit__PackRoot"] = source == "appsettings" ? "" : Packs("environment");
        start.Environment["Toolkit__Locales"] = "de,en";
        // Removing only our known keys lets the appsettings case exercise the real default provider chain.
        if (source == "appsettings")
        {
            start.Environment.Remove("Toolkit__DataRoot");
            start.Environment.Remove("Toolkit__PackRoot");
            start.Environment.Remove("Toolkit__Locales");
        }
        if (source == "commandLine")
            foreach (string argument in new[] { "--Toolkit:DataRoot", Data(source), "--Toolkit:PackRoot", Packs(source),
                "--Toolkit:Locales", "fr,en" }) start.ArgumentList.Add(argument);

        return start;
    }

    private sealed class BundleServer : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _output;
        private readonly Task<string> _errors;

        internal BundleServer(ProcessStartInfo start)
        {
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start packaged apphost.");
            _output = _process.StandardOutput.ReadToEndAsync();
            _errors = _process.StandardError.ReadToEndAsync();
        }

        internal async Task WaitUntilReadyAsync(HttpClient client)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (!timeout.IsCancellationRequested)
            {
                if (_process.HasExited)
                    Assert.Fail($"Packaged apphost exited ({_process.ExitCode}): {await _output}\n{await _errors}");
                try
                {
                    using var response = await client.GetAsync(new Uri("/api/version", UriKind.Relative), timeout.Token);
                    response.EnsureSuccessStatusCode();
                    var version = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
                    Assert.False(string.IsNullOrWhiteSpace(version.GetProperty("version").GetString()));
                    return;
                }
                catch (HttpRequestException) { await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token); }
            }
            Assert.Fail("Packaged apphost did not serve /api/version at the --urls override within 30 seconds.");
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(_output, _errors);
            _process.Dispose();
        }
    }
}
