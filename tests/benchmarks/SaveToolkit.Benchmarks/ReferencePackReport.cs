using System.Diagnostics;
using System.Text.Json;

namespace SaveToolkit.Benchmarks;

/// <summary>Five fresh-process observations of startup, first pack access and live content.</summary>
internal static class ReferencePackReport
{
    private const int Repetitions = 5;
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string scratch)
    {
        scratch = Path.GetFullPath(scratch);
        Directory.CreateDirectory(scratch);
        var observations = new List<JsonElement>();
        for (var run = 0; run < Repetitions; run++)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
            };
            start.ArgumentList.Add(typeof(ReferencePackReport).Assembly.Location);
            start.ArgumentList.Add("--reference-worker");
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Reference worker did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            if (process.ExitCode != 0) throw new InvalidOperationException(await error);
            observations.Add(JsonSerializer.Deserialize<JsonElement>(await output));
            Console.WriteLine($"Completed reference observation {run + 1}/{Repetitions}");
        }
        var report = new
        {
            capturedUtc = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount, serverGc = System.Runtime.GCSettings.IsServerGC,
            observations,
        };
        var path = Path.Combine(scratch, "references.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, ReportOptions));
        Console.WriteLine(path);
        return 0;
    }
}
