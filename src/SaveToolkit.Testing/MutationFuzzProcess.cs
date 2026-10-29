using System.Diagnostics;
using System.Text.Json;
using Xunit.Sdk;

namespace SaveToolkit.Testing;

/// <summary>Runs one inherited xUnit fact in isolation, so a timeout can actually stop the parser.</summary>
internal static class MutationFuzzProcess
{
    internal const string WorkerVariable = "SAVETOOLKIT_FUZZ_WORKER";
    internal const string ResultFile = "result.json";
    private const string ProgressFile = "progress.txt";
    private static readonly TimeSpan PreparationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    internal static void Progress(string folder, string description) =>
        File.WriteAllText(Path.Combine(folder, ProgressFile), description);

    internal static async Task<MutationFuzzResult> RunAsync(Type testClass, TimeSpan caseTimeout,
        TimeSpan? timeBudget = null)
    {
        var folder = Path.Combine(TestScratch.ProcessFolder, "mutation-fuzz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { testClass.Assembly.Location, "-method", testClass.FullName + ".MutationFuzz",
            "-noLogo", "-noColor", "-noAutoReporters", "-parallelMode", "none" })
            start.ArgumentList.Add(argument);
        start.Environment[WorkerVariable] = folder;
        var workerScratch = Path.Combine(folder, "scratch");
        Directory.CreateDirectory(workerScratch);
        start.Environment[TestScratch.RootVariable] = workerScratch;
        foreach (var variable in new[] { "TEMP", "TMP", "TMPDIR" }) start.Environment[variable] = workerScratch;
        // Fatal runtime failures must not write dumps or mutated memory into the checkout.
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        using var process = Process.Start(start) ?? throw new XunitException("Could not start mutation fuzz worker.");
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        var lastProgress = "Preparing fuzz worker";
        var elapsed = Stopwatch.StartNew();
        var totalElapsed = Stopwatch.StartNew();
        try
        {
            while (!process.HasExited)
            {
                var progress = ReadProgress(folder);
                if (progress is { Length: > 0 } && progress != lastProgress)
                {
                    lastProgress = progress;
                    elapsed.Restart();
                }

                var limit = lastProgress.StartsWith("case ", StringComparison.Ordinal) ? caseTimeout : PreparationTimeout;
                if (timeBudget is { } totalLimit && totalElapsed.Elapsed > totalLimit)
                    throw new XunitException($"MutationFuzz exceeded per-plugin budget {totalLimit}: {lastProgress}");
                if (elapsed.Elapsed > limit)
                    throw new XunitException($"MutationFuzz exceeded {limit}: {lastProgress}");
                await Task.Delay(PollInterval);
            }

            var resultPath = Path.Combine(folder, ResultFile);
            lastProgress = ReadProgress(folder) ?? lastProgress;
            if (process.ExitCode != 0 || !File.Exists(resultPath))
            {
                // Runner output can include exception messages containing private save values; only expose the checkpoint.
                throw new XunitException($"MutationFuzz worker exited {process.ExitCode} without a result: {lastProgress}");
            }

            return JsonSerializer.Deserialize<MutationFuzzResult>(await File.ReadAllTextAsync(resultPath))
                ?? throw new XunitException("MutationFuzz worker returned an empty result.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string? ReadProgress(string folder)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(folder, ProgressFile), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException) { return null; } // The worker may be replacing the checkpoint right now.
    }
}
