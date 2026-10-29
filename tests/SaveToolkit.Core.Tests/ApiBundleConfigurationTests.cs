using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Tests.References;

namespace SaveToolkit.Core.Tests;

/// <summary>Run against an extracted self-contained bundle via SAVETOOLKIT_TEST_BUNDLE.</summary>
public sealed class ApiBundleConfigurationTests
{
    private const string BindUrl = "http://127.0.0.1:0";
    private const string AppsettingsUrl = "http://127.0.0.1:1";
    private const string EnvironmentUrl = "http://127.0.0.1:2";
    private const string ListeningPrefix = "Now listening on: ";
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
        var start = CreateStartInfo(executable, bundle, root, source);
        await using var server = new BundleServer(start);
        var ct = TestContext.Current.CancellationToken;
        var url = await server.WaitUntilListeningAsync(ct);
        Assert.True(url.Port > 0, "The apphost must bind the requested ephemeral loopback port.");
        Assert.NotEqual(new Uri(AppsettingsUrl).Port, url.Port);
        Assert.NotEqual(new Uri(EnvironmentUrl).Port, url.Port);
        using var client = new HttpClient { BaseAddress = url, Timeout = Timeout.InfiniteTimeSpan };
        using var versionResponse = await client.GetAsync(new Uri("/api/version", UriKind.Relative), ct);
        versionResponse.EnsureSuccessStatusCode();
        var version = await versionResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.False(string.IsNullOrWhiteSpace(version.GetProperty("version").GetString()));
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

    [Theory]
    [InlineData("Now listening on: http://127.0.0.1:5180", 5180)]
    [InlineData("      Now listening on: http://127.0.0.1:42001/", 42001)]
    public void ListeningLineReportsTheAllocatedLoopbackPort(string line, int port)
    {
        var url = Assert.IsType<Uri>(ListeningAddress(line));
        Assert.Equal(IPAddress.Loopback.ToString(), url.Host);
        Assert.Equal(port, url.Port);
    }

    [Theory]
    [InlineData("Application started. Press Ctrl+C to shut down.")]
    [InlineData("Now listening on: http://127.0.0.1:0")]
    [InlineData("Now listening on: http://127.0.0.2:5180")]
    [InlineData("Now listening on: http://localhost:5180")]
    [InlineData("Now listening on: http://127.1:5180")]
    [InlineData("Now listening on: https://127.0.0.1:5180")]
    [InlineData("Now listening on: http://127.0.0.1:5180/api/version")]
    [InlineData("Now listening on: http://127.0.0.1:5180/?query=value")]
    [InlineData("Now listening on: http://127.0.0.1:5180/#fragment")]
    [InlineData("Now listening on: http://127.0.0.1:65536")]
    [InlineData("Now listening on: http://127.0.0.1:5180@localhost:80")]
    [InlineData("Now listening on: http://127.0.0.1:5180@127.0.0.1:80")]
    public void OtherLinesDoNotEstablishListeningReadiness(string line) => Assert.Null(ListeningAddress(line));

    private static Uri? ListeningAddress(string line)
    {
        line = line.Trim();
        if (!line.StartsWith(ListeningPrefix, StringComparison.Ordinal)) return null;
        var address = line[ListeningPrefix.Length..];
        return address.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)
            && Uri.TryCreate(address, UriKind.Absolute, out var url)
            && url.Host == IPAddress.Loopback.ToString() && url.UserInfo.Length == 0
            && url.Port > 0 && url.AbsolutePath == "/" && url.Query.Length == 0 && url.Fragment.Length == 0
                ? url : null;
    }

    private static ProcessStartInfo CreateStartInfo(string executable, string bundle, string root, string source)
    {
        string Data(string provider) => Path.Combine(root, provider, "workspaces");
        string Packs(string provider) => Path.Combine(root, provider, "reference-packs");
        File.WriteAllText(Path.Combine(root, "appsettings.json"), JsonSerializer.Serialize(new
        {
            Urls = AppsettingsUrl,
            Toolkit = new { DataRoot = Data("appsettings"), PackRoot = Packs("appsettings"), Locales = "en" },
        }));
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { "--urls", BindUrl, "--contentRoot", root,
            "--webroot", Path.Combine(bundle, "wwwroot") }) start.ArgumentList.Add(argument);
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["URLS"] = EnvironmentUrl;
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
        private readonly Task _exited;
        private readonly TaskCompletionSource<Uri> _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal BundleServer(ProcessStartInfo start)
        {
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start packaged apphost.");
            _output = ReadOutputAsync();
            _errors = _process.StandardError.ReadToEndAsync();
            _exited = _process.WaitForExitAsync();
        }

        internal async Task<Uri> WaitUntilListeningAsync(CancellationToken cancellationToken)
        {
            await Task.WhenAny(_listening.Task, _exited).WaitAsync(cancellationToken);
            if (_process.HasExited)
                Assert.Fail($"Packaged apphost exited ({_process.ExitCode}) before readiness: {await _output}\n{await _errors}");
            return await _listening.Task;
        }

        private async Task<string> ReadOutputAsync()
        {
            var output = new StringBuilder();
            try
            {
                while (await _process.StandardOutput.ReadLineAsync() is { } line)
                {
                    output.AppendLine(line);
                    if (ListeningAddress(line) is { } url) _listening.TrySetResult(url);
                }
                return output.ToString();
            }
            catch (Exception error)
            {
                _listening.TrySetException(error);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
                await _exited;
                await Task.WhenAll(_output, _errors);
            }
            finally { _process.Dispose(); }
        }
    }
}
