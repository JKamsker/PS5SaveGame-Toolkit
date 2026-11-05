using System.Diagnostics;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class FullViewPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void SyntheticFullWalkScalesWithRenderedNodes()
    {
        // Timing is diagnostic; allocation per rendered node is the portable CI guard.
        var small = Measure(2_000);
        var large = Measure(4_000);
        Assert.InRange((double)large.Nodes / small.Nodes, 1.9, 2.1);
        Assert.True(large.Bytes < small.Bytes * 2.3, "Full traversal allocations grew faster than rendered work.");
    }

    [Fact]
    public void LargestSampleSeparatesProviderWalkFromHostReview()
    {
        var plugin = new PragmataPlugin();
        var file = SampleEvidenceTests.Files().MaxBy(file => file.Length)!;
        var references = new ReferencePackStore([plugin]).Pin(plugin, file.Directory.TitleId);
        using var document = plugin.Open(file, references);
        var start = Stopwatch.GetTimestamp();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var nodes = Walk(document);
        output.WriteLine($"Provider cold walk: {nodes} nodes; {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms; {GC.GetAllocatedBytesForCurrentThread() - before} B.");
        using var session = new DocumentSession("round3-review", (SaveToolkit.Core.Storage.DiskSaveFile)file, plugin, document, references: references);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(1));
        for (var run = 0; run < 2; run++)
        {
            start = Stopwatch.GetTimestamp();
            before = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                var log = session.GetChanges(cancellationToken: deadline.Token);
                Assert.Empty(log.Changes);
                output.WriteLine($"Host review {run}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms; {GC.GetAllocatedBytesForCurrentThread() - before} B; complete={log.Complete}.");
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                // Host performance is diagnostic here; the caller's cancellation remains a failure.
                output.WriteLine("Host review exceeded the shared 60-second diagnostic deadline.");
                break;
            }
        }
    }

    private (int Nodes, long Bytes) Measure(int rows)
    {
        using var document = new PragmataDocument("data000.bin", SyntheticInventory.Create(rows));
        // Warm JIT and immutable paths before measuring a second complete rendering.
        _ = Walk(document);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var nodes = Walk(document);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"{rows} rows: {nodes} nodes; {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms; {bytes} B.");
        return (nodes, bytes);
    }

    private static int Walk(ISaveDocument document)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        Visit(document.BuildView());
        return count;
        void Visit(ViewNode node)
        {
            count++;
            IEnumerable<ViewNode> children = node switch
            {
                SectionNode { LazyId: { } id } when expanded.Add(id) => document.ExpandNode(id),
                SectionNode { LazyId: null } section => section.Children,
                TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
                TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
                _ => [],
            };
            foreach (var child in children) Visit(child);
            if (node is not QueryableTableNode tableNode || !tables.Add(tableNode.CollectionId)) return;
            var queryable = (IQueryableDocument)document;
            for (var offset = 0; ; offset += QueryableCollectionLimits.MaxPageSize)
            {
                var page = queryable.Query(tableNode.CollectionId, new()
                { Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize, IncludeEmpty = true });
                foreach (var row in page.Rows)
                    foreach (var child in row.Cells.Values) Visit(child);
                if (offset + page.Rows.Count >= page.FilteredCount) break;
            }
        }
    }
}

