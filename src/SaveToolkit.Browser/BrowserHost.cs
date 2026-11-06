using System.Reflection;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.LocalApi;

namespace SaveToolkit.Browser;

/// <summary>Owns the single in-process API host for this WebAssembly instance.</summary>
internal static class BrowserHost
{
    /// <summary>Session data (imported workspaces) on Emscripten's in-memory file system.</summary>
    private const string DefaultDataRoot = "/savetoolkit/session";
    private const string ProblemJson = "application/problem+json";

    /// <summary>Same cadence as the Api's background sweeper, but run between requests (see DispatchAsync).</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Deferred startup work (validating embedded reference packs) waits this long after boot, so the
    /// UI's first requests are answered first, then runs one pack at a time between requests.
    /// </summary>
    private static readonly TimeSpan IdleWorkDelay = TimeSpan.FromSeconds(1);

    private static LocalApiHost? _host;
    private static int _inFlight;
    private static long _nextSweep;
    private static WireBootInfo? _boot;

    public static string Version { get; } = typeof(BrowserHost).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-local";

    public static WireBootInfo Initialize(WireInitOptions options)
    {
        if (_boot is not null) return _boot;
        var failed = PluginLoader.LoadAll(PluginManifest.AssemblyNames, message => Console.Error.WriteLine(message));
        var catalog = PluginCatalog.Discover();
        var dataRoot = string.IsNullOrWhiteSpace(options.DataRoot) ? DefaultDataRoot : options.DataRoot;
        // Presets and reference packs are durable in the Api (LocalApplicationData). Here they live
        // under the worker's IDBFS mount; workspaces stay session-scoped like the Api's temp folder.
        var durableRoot = string.IsNullOrWhiteSpace(options.PersistentRoot) ? dataRoot : options.PersistentRoot;
        _host = new LocalApiHost(new LocalApiOptions
        {
            DataRoot = dataRoot,
            PresetRoot = Path.Combine(durableRoot, "presets"),
            PackRoot = Path.Combine(durableRoot, "reference-packs"),
            Catalog = catalog,
            Version = Version,
            // No background loop: idle workspaces are swept between requests, so every operation stays
            // serialized in the worker's request order.
            SweepInterval = null,
            OnError = (context, exception) => Console.Error.WriteLine($"{context}: {exception}"),
        });
        _nextSweep = Environment.TickCount64 + (long)SweepInterval.TotalMilliseconds;
        _ = RunIdleWorkAsync(_host);
        _boot = new WireBootInfo(Version, [.. catalog.Plugins.Select(p => p.Info.Id).Order(StringComparer.Ordinal)], failed);
        return _boot;
    }

    public static async Task<byte[]> DispatchAsync(string method, string url, string headersJson, byte[] body)
    {
        if (_host is null) return Problem(503, "The local runtime has not been initialized.");
        SweepIfDue(_host);
        IReadOnlyList<KeyValuePair<string, string>> headers;
        try
        {
            headers = WireFormat.ParseHeaders(headersJson);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return Problem(400, "The request headers could not be read.");
        }

        _inFlight++;
        try
        {
            var response = await _host.SendAsync(new LocalRequest(method, url, headers, body));
            return WireFormat.Frame(response.Status, response.Headers, response.Body);
        }
        catch (Exception exception)
        {
            // The router maps domain failures itself; anything reaching here is a host bug.
            await Console.Error.WriteLineAsync($"Unhandled local API failure for {method} {url}: {exception}");
            return Problem(500, "An unexpected error occurred.");
        }
        finally
        {
            _inFlight--;
        }
    }

    /// <summary>
    /// Validates the remaining embedded reference packs in the background. Each step is one pack; the
    /// timer turn between steps lets queued requests run first. Results never depend on this: anything a
    /// request needs is validated on demand, with the same outcome.
    /// </summary>
    private static async Task RunIdleWorkAsync(LocalApiHost host)
    {
        try
        {
            await Task.Delay(IdleWorkDelay);
            while (true)
            {
                if (_inFlight > 0)
                {
                    await Task.Delay(50);
                    continue;
                }

                if (!host.RunIdleWork()) return;
                await Task.Delay(1);
            }
        }
        catch (Exception exception)
        {
            // Background work must never take the runtime down; requests still validate on demand.
            await Console.Error.WriteLineAsync($"Background reference pack validation stopped: {exception}");
        }
    }

    private static void SweepIfDue(LocalApiHost host)
    {
        if (Environment.TickCount64 < _nextSweep) return;
        _nextSweep = Environment.TickCount64 + (long)SweepInterval.TotalMilliseconds;
        try
        {
            host.SweepIdleWorkspaces();
        }
        catch (Exception exception)
        {
            // Cleanup must never fail the request that triggered it.
            Console.Error.WriteLine($"Workspace sweep failed: {exception}");
        }
    }

    private static byte[] Problem(int status, string title)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["title"] = title, ["status"] = status });
        return WireFormat.Frame(status, [new("Content-Type", ProblemJson)], body);
    }
}
