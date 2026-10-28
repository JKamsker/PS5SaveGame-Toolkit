using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartCollectionTests
{
    [Fact]
    public void RecordQueriesSearchBeforePagingAndUseStableKeys()
    {
        var bytes = SlotFixture.Create(plain: PlainFixture.Create(repeatedRecords: 250));
        using var document = new AtomicHeartPlugin().Open(MemorySaveSet.SingleFile("slot", bytes));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var info = Assert.Single(queryable.Collections);
        var page = queryable.Query(info.Id, new() { Text = "Subsystem2", SortColumn = "name", Limit = 1 });
        Assert.Equal(250, page.TotalCount);
        Assert.Equal(61, page.FilteredCount); // 2, 20..29, 200..249.
        var first = Assert.Single(page.Rows);
        var second = Assert.Single(queryable.Query(info.Id, new() { Text = "Subsystem2", SortColumn = "name", Limit = 1, Offset = 1 }).Rows);
        Assert.NotEqual(first.Key, second.Key);
        Assert.Equal(first.Key, Assert.Single(queryable.Query(info.Id, new() { Text = "Subsystem2", SortColumn = "name", Limit = 1 }).Rows).Key);
        var details = Assert.IsType<SectionNode>(first.Cells["details"]);
        Assert.NotEmpty(document.ExpandNode(details.LazyId!));
        var bounded = queryable.Query(info.Id, new() { Limit = int.MaxValue, Offset = -1 });
        Assert.Equal(200, bounded.Rows.Count);
        Assert.Equal(0, bounded.Offset);
        Assert.Throws<KeyNotFoundException>(() => queryable.Query("unknown", new()));
        Assert.Throws<ArgumentException>(() => queryable.Query(info.Id, new() { SortColumn = "missing" }));
        Assert.Throws<ArgumentException>(() => queryable.Query(info.Id, new() { Cursor = "unknown" }));
        Assert.Throws<ArgumentException>(() => queryable.Query(info.Id, new() { Filters = new Dictionary<string, string> { ["missing"] = "x" } }));
    }

    [Fact]
    public void RealRecordSearchIncludesUnloadedPlayerComponents()
    {
        var file = SampleSaves.Load("PPSA10695").AllDiskFiles.First(f => !f.IsSystemFile);
        using var document = new AtomicHeartPlugin().Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var info = Assert.Single(queryable.Collections);
        var page = queryable.Query(info.Id, new() { Text = "BP_PlayerInventory", Filters = new Dictionary<string, string> { ["kind"] = "component" } });
        Assert.Equal(4063, page.TotalCount);
        Assert.Equal(1, page.FilteredCount);
        var row = Assert.Single(page.Rows);
        Assert.Equal("BP_PlayerInventory_C_0", Assert.IsType<TextNode>(row.Cells["name"]).Text);
        var details = Assert.IsType<SectionNode>(row.Cells["details"]);
        Assert.NotEmpty(document.ExpandNode(details.LazyId!));
        var actors = queryable.Query(info.Id, new() { Filters = new Dictionary<string, string> { ["kind"] = "actor" }, Limit = 3, SortColumn = "bytes", Descending = true });
        Assert.Equal(3502, actors.FilteredCount);
        Assert.Equal(3, actors.Rows.Count);
        var lengths = actors.Rows.Select(r => int.Parse(Assert.IsType<TextNode>(r.Cells["bytes"]).Text)).ToArray();
        Assert.Equal(lengths.OrderDescending(), lengths);
    }
}
