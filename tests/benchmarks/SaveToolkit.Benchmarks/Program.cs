using BenchmarkDotNet.Running;

namespace SaveToolkit.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--reference-worker"])
        {
            ReferencePackWorker.Run();
            return 0;
        }
        if (args is ["--references", var referenceScratch])
            return ReferencePackReport.RunAsync(referenceScratch).GetAwaiter().GetResult();
        if (args is ["--startup-worker", var workerScratch])
        {
            StartupWorker.Run(workerScratch);
            return 0;
        }
        if (args is ["--startup", var apiPath, var scratch])
            return StartupReport.RunAsync(apiPath, scratch).GetAwaiter().GetResult();
        if (args is ["--verify"])
        {
            foreach (var size in Enum.GetValues<DataSize>())
                foreach (var format in Enum.GetValues<SaveFormat>())
                {
                    using var fixture = new BenchmarkFixture(size, format);
                    Console.WriteLine($"PASS {size}/{format}: import, probe, page, review, undo, spooled export");
                }
            return 0;
        }
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args).ToArray();
        // BenchmarkDotNet can return normally after a validation/build/workload failure.
        return summaries.Length > 0 && summaries.All(summary => !summary.HasCriticalValidationErrors
            && summary.Reports.Length > 0 && summary.Reports.All(report => report.Success)) ? 0 : 1;
    }
}
