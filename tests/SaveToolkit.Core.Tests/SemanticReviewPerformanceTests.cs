using System.Diagnostics;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewPerformanceTests(ITestOutputHelper output) : IDisposable
{
    private const int Rows = 10_000;
    private const int Repetitions = 7;
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void FullBudgetReviewHasLinearAllocationAndTimeBounds(int edits)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)));
        var workspace = _store.Create("review-perf", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var current = new SemanticQueryableDocument(Rows) { DuplicateTables = true };
        using var session = new DocumentSession("review", workspace.Set.AllDiskFiles.Single(), new CounterPlugin(), current,
            openOriginal: () => new SemanticQueryableDocument(Rows) { DuplicateTables = true });
        for (var i = 0; i < edits; i++) current.SetQuantity(0, i * Rows / Math.Max(1, edits), -1);
        for (var i = 0; i < 5; i++) _ = session.GetChanges();
        var bestBytes = long.MaxValue;
        var bestTime = TimeSpan.MaxValue;
        for (var i = 0; i < Repetitions; i++)
        {
            var pagesBefore = current.Queries.Count;
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var log = session.GetChanges();
            var elapsed = Stopwatch.GetElapsedTime(start);
            bestBytes = Math.Min(bestBytes, GC.GetAllocatedBytesForCurrentThread() - before);
            if (elapsed < bestTime) bestTime = elapsed;
            Assert.True(log.Complete);
            Assert.Empty(log.Warnings);
            Assert.Equal(edits, log.Changes.Count);
            Assert.Equal(Rows / 200, current.Queries.Count - pagesBefore);
        }

        output.WriteLine($"{edits} edits: {bestBytes} B; {bestTime.TotalMilliseconds:F3} ms");
        // Generous per-request time bound detects accidental quadratic work, tolerating JIT/CI variance.
        Assert.True(bestTime < TimeSpan.FromSeconds(2), $"Review took {bestTime}.");
        Assert.True(bestBytes < 20_000_000, $"Review allocated {bestBytes:N0} B.");
    }
}
