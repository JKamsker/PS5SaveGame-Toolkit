using System.Diagnostics;
using System.Runtime.CompilerServices;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.References;

[Collection(typeof(AllocationTestCollection))]
public sealed class LazyReferenceRetentionTests(ITestOutputHelper output)
{
    private const int ExpectedPackCount = 40;
    private const long IdleRetainedBudget = 16 * 1024 * 1024;
    private const string WorkerVariable = "SAVETOOLKIT_REFERENCE_RETENTION_WORKER";
    private const string WorkerClass = "SaveToolkit.Core.Tests.References.LazyReferenceRetentionTests";

    [Fact]
    public async Task CatalogStartupDoesNotRetainUnusedEmbeddedRecordGraphsOrAssetBuffers()
    {
        // In-process API tests can leave background work alive beyond their collection. Run this
        // process-wide bound in a fresh test host so such work cannot make the budget order-dependent.
        if (Environment.GetEnvironmentVariable(WorkerVariable) != "1")
        {
            await RunIsolated();
            return;
        }

        // Discover/JIT and serializer caches are warmed outside the retained-memory boundary.
        var plugins = EmbeddedPackSnapshotTests.Plugins();
        Warm(plugins);
        var retained = new long[3];
        for (var index = 0; index < retained.Length; index++) retained[index] = MeasureRetained(plugins);
        output.WriteLine($"Idle embedded packs retained B: [{string.Join(", ", retained)}]; budget {IdleRetainedBudget:N0} B.");
#if DEBUG
        output.WriteLine("Debug retained-memory report (bound enforced in Release only).");
#else
        Assert.True(retained.Min() <= IdleRetainedBudget,
            $"Unused embedded packs retained {retained.Min():N0} B; budget {IdleRetainedBudget:N0} B. " +
            $"All process-wide samples: [{string.Join(", ", retained)}].");
#endif
    }

    private async Task RunIsolated()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(typeof(LazyReferenceRetentionTests).Assembly.Location);
        start.ArgumentList.Add("-class");
        start.ArgumentList.Add(WorkerClass);
        start.ArgumentList.Add("-showLiveOutput");
        start.Environment[WorkerVariable] = "1";
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("Could not start retained-memory worker.");
        var stdout = worker.StandardOutput.ReadToEndAsync();
        var stderr = worker.StandardError.ReadToEndAsync();
        try { await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1)); }
        catch (TimeoutException)
        {
            worker.Kill(entireProcessTree: true);
            throw;
        }

        var report = await stdout + await stderr;
        output.WriteLine(report);
        Assert.True(worker.ExitCode == 0, $"Isolated retained-memory regression failed with exit {worker.ExitCode}.\n{report}");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Warm(IReadOnlyList<IGamePlugin> plugins)
    {
        var store = new ReferencePackStore(plugins);
        Assert.Empty(store.LoadErrors);
        Assert.Equal(ExpectedPackCount, store.Packs.Count);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureRetained(IReadOnlyList<IGamePlugin> plugins)
    {
        var before = Collect();
        var store = new ReferencePackStore(plugins);
        Assert.Empty(store.LoadErrors);
        Assert.Equal(ExpectedPackCount, store.Packs.Count);
        Assert.All(store.Packs, pack => Assert.NotEmpty(pack.Info.Datasets));
        var retained = Math.Max(0, Collect() - before);
        GC.KeepAlive(store);
        return retained;
    }

    private static long Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
