using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class QueryableEvidenceTests
{
    [Fact]
    public void AllSampleProgressAndWorldListsAreQueryableEditableAndLossless()
    {
        foreach (var file in SampleEvidenceTests.Files().Where(file => file.FileName != "data00-1.bin"))
        {
            using var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
            var tabs = Assert.IsType<TabsNode>(document.BuildView()).Tabs;
            foreach (var title in new[] { "Progress", "Quests", "World" })
            {
                var tab = tabs.Single(tab => tab.Title == title);
                var stats = Assert.Single(tab.Children.OfType<StatsNode>());
                Assert.NotEmpty(stats.Items);
                Assert.All(stats.Items, stat => Assert.Contains("Stored records only", stat.Hint!, StringComparison.Ordinal));
                foreach (var section in tab.Children.OfType<SectionNode>())
                {
                    var children = document.ExpandNode(section.LazyId!);
                    foreach (var table in children.OfType<QueryableTableNode>())
                    {
                        var page = document.Query(table.CollectionId, new QueryableCollectionQuery { Limit = 7 });
                        Assert.Equal(table.TotalCount, page.TotalCount);
                        Assert.InRange(page.Rows.Count, 0, 7);
                        Assert.Contains("Unknown", table.Description!, StringComparison.Ordinal);
                        foreach (var row in page.Rows)
                        {
                            var detail = row.Cells["details"];
                            var fields = detail is SectionNode lazy ? document.ExpandNode(lazy.LazyId!).OfType<FieldNode>()
                                : new[] { detail }.OfType<FieldNode>();
                            Assert.All(fields, field =>
                            {
                                Assert.False(field.Spec.ReadOnly);
                                Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
                            });
                        }
                    }
                }
            }
            Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        }
    }

    [Fact]
    public void ThousandsOfWorldRecordsHaveCompletePagingSearchFiltersAndDeterministicSorting()
    {
        using var document = EditingTests.Gameplay();
        var table = WorldRecords(document);
        Assert.True(table.TotalCount > QueryableCollectionLimits.MaxPageSize);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var query = new QueryableCollectionQuery { Limit = QueryableCollectionLimits.MaxPageSize };
        TableRow? chosen = null;
        for (var offset = 0; offset < table.TotalCount; offset += query.Limit)
        {
            var page = document.Query(table.CollectionId, query with { Offset = offset });
            Assert.Equal(table.TotalCount, page.FilteredCount);
            Assert.All(page.Rows, row => Assert.True(keys.Add(row.Key)));
            chosen ??= page.Rows.First();
        }
        Assert.Equal(table.TotalCount, keys.Count);
        var identity = ((TextNode)chosen!.Cells["identity"]).Text;
        var search = document.Query(table.CollectionId, query with { Text = identity });
        Assert.Contains(search.Rows, row => row.Key == chosen.Key);
        var type = ((TextNode)chosen.Cells["type"]).Text;
        var filter = document.Query(table.CollectionId, query with { Filters = new Dictionary<string, string> { ["type"] = type } });
        Assert.All(filter.Rows, row => Assert.Equal(type, ((TextNode)row.Cells["type"]).Text));
        Assert.Equal(0, document.Query(table.CollectionId, query with { Text = "does-not-exist-in-a-Pragmata-record" }).FilteredCount);
        Assert.Empty(document.Query(table.CollectionId, query with { Offset = int.MaxValue }).Rows);
        var descending = document.Query(table.CollectionId, query with { SortColumn = "type", Descending = true });
        Assert.Equal(descending.Rows.Select(row => row.Key),
            document.Query(table.CollectionId, query with { SortColumn = "type", Descending = true }).Rows.Select(row => row.Key));
        var array = document.Layout.Sections.SelectMany(section => section.Children).Single(node => $"evidence/array-at-{node.Offset:x}" == table.CollectionId);
        array.Children = array.Children.Reverse().ToArray();
        Assert.Equal(descending.Rows.Select(row => row.Key),
            document.Query(table.CollectionId, query with { SortColumn = "type", Descending = true }).Rows.Select(row => row.Key));
    }

    [Fact]
    public void FrameworkServesPagesAndRejectsMalformedQueries()
    {
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(file => file.FileName == "data000.bin");
        using var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
        using var session = new DocumentSession("evidence", file, new PragmataPlugin(), document);
        var world = ((TabsNode)session.BuildView()).Tabs.Single(tab => tab.Title == "World");
        var table = world.Children.OfType<SectionNode>().SelectMany(section => session.Expand(section.LazyId!))
            .OfType<QueryableTableNode>().OrderByDescending(node => node.TotalCount).First();
        var page = session.QueryCollection(table.CollectionId, new QueryableCollectionQuery { Limit = 5 }, session.State.Revision);
        Assert.Equal(5, page.Rows.Count);
        foreach (var query in new[]
        {
            new QueryableCollectionQuery { Limit = 201 }, new QueryableCollectionQuery { Limit = 0 },
            new QueryableCollectionQuery { Offset = -1 }, new QueryableCollectionQuery { Cursor = "invalid" },
            new QueryableCollectionQuery { SortColumn = "missing" }, new QueryableCollectionQuery { Text = new string('x', 257) },
            new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["missing"] = "x" } },
        }) Assert.ThrowsAny<ArgumentException>(() => document.Query(table.CollectionId, query));
        Assert.Throws<KeyNotFoundException>(() => document.Query("missing", new QueryableCollectionQuery()));
    }

    private static QueryableTableNode WorldRecords(PragmataDocument document) => ((TabsNode)document.BuildView()).Tabs
        .Single(tab => tab.Title == "World").Children.OfType<SectionNode>()
        .SelectMany(section => document.ExpandNode(section.LazyId!)).OfType<QueryableTableNode>()
        .OrderByDescending(table => table.TotalCount).First();
}
