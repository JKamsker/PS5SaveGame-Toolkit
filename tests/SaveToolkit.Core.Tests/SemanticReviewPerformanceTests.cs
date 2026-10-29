using System.Diagnostics;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class SemanticReviewPerformanceTests(ITestOutputHelper output) : IDisposable
{
    private const int Rows = 10_000;
    private const int Repetitions = 7;
    private const int WarmupIterations = 5;
    private const long AllocationLimit = 20_000_000;
    private const int MaximumReviewMilliseconds = 2_000;
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
        var bestTime = TimeSpan.MaxValue;
        var run = 0;
        var pagesBefore = 0;
        SemanticChangeLog? log = null;
        var allocation = AllocationProbe.Measure(() =>
        {
            var start = Stopwatch.GetTimestamp();
            log = session.GetChanges();
            var elapsed = Stopwatch.GetElapsedTime(start);
            if (run > WarmupIterations && elapsed < bestTime) bestTime = elapsed;
        }, warmupRuns: WarmupIterations, measuredRuns: Repetitions, beforeRun: () =>
        {
            run++;
            log = null;
            pagesBefore = current.Queries.Count;
        }, afterRun: () =>
        {
            if (log is null) return; // Preserve the original exception if the operation failed.
            Assert.True(log.Complete);
            Assert.Empty(log.Warnings);
            Assert.Equal(edits, log.Changes.Count);
            Assert.Equal(Rows / QueryableCollectionLimits.MaxPageSize, current.Queries.Count - pagesBefore);
        });

        output.WriteLine($"{edits} edits: {allocation.MinimumBytes} B; {bestTime.TotalMilliseconds:F3} ms; Release time bound {MaximumReviewMilliseconds} ms");
        // Generous per-request time bound detects accidental quadratic work, tolerating JIT/CI variance.
#if !DEBUG
        Assert.True(bestTime.TotalMilliseconds < MaximumReviewMilliseconds, $"Review took {bestTime}.");
#endif
        allocation.AssertLessThan(AllocationLimit, "One full-budget semantic review.");
    }
}
