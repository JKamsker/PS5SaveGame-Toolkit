using System.Text;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSearchReviewTests
{
    [Fact]
    public void SearchFindsCollapsedLateRowsAndReflectsEditsAndUndo()
    {
        var source = "[" + string.Join(',', Enumerable.Range(0, 20_000).Select(i => $"{{\"key\":\"entry-{i:D5}\"}}")) + "]";
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("data.json", Encoding.UTF8.GetBytes(source)));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = query.Query("json-tree", new() { Text = "entry-19999", Limit = 200 });
        Assert.Equal(40_001, page.TotalCount);
        var row = Assert.Single(page.Rows);
        Assert.Contains("[19999]", Assert.IsType<TextNode>(row.Cells["path"]).Text, StringComparison.Ordinal);
        var field = Assert.IsType<FieldNode>(row.Cells["value"]);
        var outcome = document.ApplyEdits([new(field.ValueId, JsonEditingTests.Text("new-live-value"))]);
        Assert.True(outcome.Succeeded);
        Assert.Empty(query.Query("json-tree", new() { Text = "entry-19999" }).Rows);
        Assert.Single(query.Query("json-tree", new() { Text = "new-live-value" }).Rows);
        Assert.True(document.ApplyEdits([new(field.ValueId, outcome.Changes[0].OldValue)]).Succeeded);
        Assert.Single(query.Query("json-tree", new() { Text = "entry-19999" }).Rows);
        Assert.Equal(200, query.Query("json-tree", new() { Limit = int.MaxValue }).Rows.Count);
    }

    [Fact]
    public void SearchDistinguishesDuplicateOccurrencesAndNullFromEmpty()
    {
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("data.json", "{\"a/b\":null,\"a/b\":\"\",\"a/b\":-0}"u8.ToArray()));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = query.Query("json-tree", new() { Text = "a/b", Limit = 1, Offset = 1 });
        Assert.Equal(3, page.FilteredCount);
        Assert.Contains("#2", Assert.IsType<TextNode>(Assert.Single(page.Rows).Cells["path"]).Text, StringComparison.Ordinal);
        var nulls = query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["type"] = "null" } });
        Assert.Single(nulls.Rows);
        Assert.True(Assert.IsType<FieldNode>(nulls.Rows[0].Cells["value"]).Value.IsNull);
        var sorted = query.Query("json-tree", new() { SortColumn = "type", Descending = true });
        Assert.Equal(4, sorted.Rows.Select(r => r.Key).Distinct().Count());
        Assert.Throws<ArgumentException>(() => query.Query("json-tree", new() { Cursor = "forged" }));
    }

    [Fact]
    public void FrameworkChangesReviewUsesOriginalAcrossRefetchAndHistoryEviction()
    {
        const string source = "{\"shown\":-0,\"hidden\":{\"n\":1.0000,\"s\":\"1\",\"null\":null}}";
        using var fixture = new JsonSessionFixture(source, new DocumentSessionOptions { MaxHistory = 1 });
        var root = Assert.IsType<TabsNode>(fixture.Session.BuildView()).Tabs.Single(t => t.Title == "JSON").Children[0];
        var lazy = Assert.IsType<SectionNode>(root).LazyId!;
        var shown = Assert.IsType<FieldNode>(fixture.Session.Expand(lazy)[0]);
        Assert.True(fixture.Edit(shown.ValueId, JsonEditingTests.Number("0")).Ok);
        // Hidden fields were never expanded by the client; Core opens a separate original baseline.
        var first = fixture.Session.GetChanges();
        Assert.True(first.Complete);
        Assert.Equal("-0", Assert.Single(first.Changes).OriginalValue.AsUnion().Value.AsExactNumber());
        var hidden = fixture.Fields().Single(f => f.Label == "n");
        Assert.True(fixture.Edit(hidden.ValueId, JsonEditingTests.Text("1.0000")).Ok);
        _ = fixture.Session.BuildView();
        var log = fixture.Session.GetChanges();
        Assert.True(log.Complete);
        Assert.Equal(2, log.Changes.Count);
        var typeChange = Assert.Single(log.Changes, c => c.ValueId == hidden.ValueId);
        Assert.Equal("number", typeChange.OriginalValue.AsUnion().Type);
        Assert.Equal("string", typeChange.CurrentValue.AsUnion().Type);
        Assert.True(fixture.Session.RevertChange(shown.ValueId, log.Revision).Ok);
        Assert.Single(fixture.Session.GetChanges().Changes);
        Assert.True(fixture.Session.RevertChange(hidden.ValueId, fixture.Session.State.Revision).Ok);
        Assert.Empty(fixture.Session.GetChanges().Changes);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }
}
