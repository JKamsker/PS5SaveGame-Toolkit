using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SaveToolkit.Benchmarks;

/// <summary>Opt-in process startup observations, deliberately separate from warmed BDN timings.</summary>
internal static class StartupReport
{
    private const int Repetitions = 5;
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string apiPath, string scratch)
    {
        apiPath = Path.GetFullPath(apiPath);
        scratch = Path.GetFullPath(scratch);
        Directory.CreateDirectory(scratch);
        var observations = new List<object>();
        for (var run = 0; run < Repetitions; run++)
        {
            var start = StartInfo(typeof(StartupReport).Assembly.Location, "--startup-worker", scratch);
            using var worker = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
            var output = worker.StandardOutput.ReadToEndAsync();
            var error = worker.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await worker.WaitForExitAsync(timeout.Token); }
            finally { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
            if (worker.ExitCode != 0) throw new InvalidOperationException(await error);
            var core = JsonSerializer.Deserialize<JsonElement>(await output);
            var api = await MeasureApiAsync(apiPath, scratch, run);
            observations.Add(new { run, core, api });
            Console.WriteLine($"Completed startup observation {run + 1}/{Repetitions}");
        }
        var report = new
        {
            capturedUtc = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount, apiPath, observations,
        };
        var path = Path.Combine(scratch, "startup.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, ReportOptions));
        Console.WriteLine(path);
        return 0;
    }

    private static async Task<object> MeasureApiAsync(string apiPath, string scratch, int run)
    {
        // Release the ephemeral listener immediately before starting Kestrel. Bind races fail visibly.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var root = Path.Combine(scratch, "api-" + run);
        var start = StartInfo(apiPath, "--urls", $"http://127.0.0.1:{port}",
            "--Toolkit:DataRoot", Path.Combine(root, "workspaces"),
            "--Toolkit:PackRoot", Path.Combine(root, "packs"));
        start.WorkingDirectory = Path.GetDirectoryName(apiPath)!;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
        var started = Stopwatch.GetTimestamp();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("API did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            while (true)
            {
                if (process.HasExited) throw new InvalidOperationException("API exited: " + await error);
                try
                {
                    using var response = await client.GetAsync("/api/plugins");
                    response.EnsureSuccessStatusCode();
                    var plugins = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
                    if (plugins.GetArrayLength() < 17) throw new InvalidOperationException("Missing plugins.");
                    break;
                }
                catch (HttpRequestException) when (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
                { await Task.Delay(10); }
            }
            var startupMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var startup = await SnapshotAsync(process);
            var data = new SyntheticData(DataSize.Small, SaveFormat.GenericJson);
            await StartupHttpCycle.RunAsync(client, data.ZipBytes);
            var afterOne = await SnapshotAsync(process);
            var cycles = new List<object>();
            for (var cycle = 1; cycle <= 20; cycle++)
            {
                await StartupHttpCycle.RunAsync(client, data.ZipBytes);
                cycles.Add(new { cycle, memory = await SnapshotAsync(process) });
            }
            using var remaining = await client.GetAsync("/api/workspaces");
            remaining.EnsureSuccessStatusCode();
            if (JsonSerializer.Deserialize<JsonElement>(await remaining.Content.ReadAsStringAsync()).GetArrayLength() != 0)
                throw new InvalidOperationException("Workspaces survived close.");
            return new { startupMilliseconds, startup, afterOne, cycles };
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await File.WriteAllTextAsync(Path.Combine(scratch, $"api-{run}.log"), await output + await error);
        }
    }

    private static async Task<object> SnapshotAsync(Process process)
    {
        // Natural idle only: do not trim working set or force a GC in the target process.
        await Task.Delay(500);
        process.Refresh();
        return new { process.WorkingSet64, process.PrivateMemorySize64 };
    }

    private static ProcessStartInfo StartInfo(string assembly, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(assembly);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return start;
    }
}
