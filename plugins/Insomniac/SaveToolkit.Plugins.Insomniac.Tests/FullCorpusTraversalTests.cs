using System.Diagnostics;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

/// <summary>Exhaustive sample coverage, including lazy descendants discovered inside later pages.</summary>
[Collection(typeof(AllocationTestCollection))]
public sealed class FullCorpusTraversalTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("PPSA01474")]
    public void EverySampleWalksEveryPageAndPreservesEveryByte(string title)
    {
        var plugin = new InsomniacPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, title);
        var files = SampleSaves.Load(title).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.NotEmpty(files);
        long openTicks = 0, walkTicks = 0, exportTicks = 0, allocated = 0;
        var nodes = 0;
        var pages = 0;
        foreach (var file in files)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            using var document = plugin.Open(file, references);
            openTicks += Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            var seenLazy = new HashSet<string>(StringComparer.Ordinal);
            var seenCollections = new HashSet<string>(StringComparer.Ordinal);
            var fileNodes = 0L;
            var nodeBudget = Math.Max(1_000_000L, file.Length * 8);
            Visit(document.BuildView());
            void Visit(ViewNode node)
            {
                nodes++;
                if (node is FieldNode field) Assert.Equal(field.Value, document.GetValue(field.ValueId));
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                Assert.True(++fileNodes < nodeBudget, "Traversal exceeded its finite file budget.");
                IEnumerable<ViewNode> children = node switch
                {
                    SectionNode { LazyId: { } id } when seenLazy.Add(id) => document.ExpandNode(id),
                    SectionNode { LazyId: null } section => section.Children,
                    TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children),
                    TableNode table => table.Rows.SelectMany(row => row.Cells.Values),
                    _ => [],
                };
                foreach (var child in children) Visit(child);
                if (node is not QueryableTableNode tableNode || !seenCollections.Add(tableNode.CollectionId)) return;
                var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (var offset = 0; ; offset += QueryableCollectionLimits.MaxPageSize)
                {
                    var page = queryable.Query(tableNode.CollectionId, new()
                    { Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize, IncludeEmpty = true });
                    pages++;
                    Assert.InRange(page.Rows.Count, 0, QueryableCollectionLimits.MaxPageSize);
                    foreach (var row in page.Rows)
                    {
                        Assert.True(keys.Add(row.Key));
                        foreach (var cell in row.Cells.Values) Visit(cell);
                    }
                    if (offset + page.Rows.Count >= page.FilteredCount) break;
                    Assert.NotEmpty(page.Rows);
                }
                Assert.Equal(tableNode.TotalCount, keys.Count);
            }
            walkTicks += Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            foreach (var exported in document.Serialize())
            {
                var original = file.Directory.Files.Single(candidate => candidate.RelativePath == exported.RelativePath);
                Assert.True(original.ReadAllBytes().AsSpan().SequenceEqual(exported.Content.Span), "No-op export changed bytes.");
                var reopenedFile = MemorySaveSet.SingleFile(original.FileName, exported.Content.ToArray(), title);
                using var reopened = plugin.Open(reopenedFile, references);
                _ = reopened.BuildView();
            }
            exportTicks += Stopwatch.GetTimestamp() - start;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        output.WriteLine($"{files.Length} files; {nodes} nodes; {pages} pages; open={Milliseconds(openTicks):F1} ms; walk={Milliseconds(walkTicks):F1} ms; export={Milliseconds(exportTicks):F1} ms; allocated={allocated} B.");
    }

    private static double Milliseconds(long ticks) => (double)ticks / Stopwatch.Frequency * 1000;
}
