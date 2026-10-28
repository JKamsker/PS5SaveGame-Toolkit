using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSearchNavigationTests
{
    [Fact]
    public void ImportedComparisonDistinguishesExactNumbersStringsNullAndAddedNodes()
    {
        using var fixture = new JsonSessionFixture("{\"number\":1.2300,\"text\":\"1.2300\",\"nil\":null}");
        _ = fixture.Fields();
        Assert.True(fixture.Edit("value/1", JsonEditingTests.Text("1.2300")).Ok);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(fixture.Document);
        var changed = query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["state"] = "MODIFIED" } });
        var number = Assert.Single(changed.Rows);
        Assert.Equal("1.2300", Text(number, "original"));
        Assert.Contains("\"1.2300\"", Assert.IsType<SectionNode>(number.Cells["value"]).Title, StringComparison.Ordinal);
        Assert.Equal("string", Text(number, "type"));
        var text = Assert.Single(query.Query("json-tree", new() { Text = "value/2" }).Rows);
        Assert.Equal("\"1.2300\"", Text(text, "original"));
        JsonArrayEditingTests.Apply(fixture, "object/0/insert", ("at", FieldValue.Of(0L)), ("name", FieldValue.Of("added")), ("json", FieldValue.Of("null")));
        var addition = Assert.Single(query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["state"] = "added" } }).Rows);
        Assert.Equal("(absent)", Text(addition, "original"));
        Assert.Equal("null", Text(addition, "type"));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Empty(query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["state"] = "modified" } }).Rows);
        Assert.Empty(query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["state"] = "added" } }).Rows);
    }

    [Fact]
    public void ContainerMatchesNavigateLazySubtreeAndIdentityScopesSurviveRenameButRetireOnRemoval()
    {
        using var fixture = new JsonSessionFixture("{\"group\":{\"MasterVolume\":50,\"nested\":[1,2]},\"other\":0}");
        _ = fixture.Session.BuildView();
        var page = fixture.Session.QueryCollection("json-tree", new() { Text = "value/1" });
        var container = Assert.Single(page.Rows);
        var details = Assert.IsType<SectionNode>(container.Cells["value"]);
        var subtree = Assert.IsType<SectionNode>(Assert.Single(details.Children));
        Assert.Equal("$[\"group\"]#1", subtree.Description);
        var lazy = Assert.Single(subtree.Children.OfType<SectionNode>(), section => section.LazyId is not null);
        var volume = Assert.IsType<FieldNode>(fixture.Session.Expand(lazy.LazyId!)[0]);
        Assert.True(fixture.Edit(volume.ValueId, JsonEditingTests.Number("55")).Ok);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(fixture.Document);
        Assert.Single(query.Query("json-tree", new() { Text = "Master Volume" }).Rows);
        var scoped = query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["within"] = "value/1" } });
        Assert.Equal(5, scoped.Rows.Count);
        Assert.DoesNotContain(scoped.Rows, row => Text(row, "path").Contains("other", StringComparison.Ordinal));
        JsonArrayEditingTests.Apply(fixture, "object/0/rename", ("at", FieldValue.Of(0L)), ("name", FieldValue.Of("renamed")));
        Assert.Equal(5, query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["within"] = "value/1" } }).Rows.Count);
        JsonArrayEditingTests.Apply(fixture, "object/0/remove", ("at", FieldValue.Of(0L)));
        Assert.Empty(query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["within"] = "value/1" } }).Rows);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(5, query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["within"] = "value/1" } }).Rows.Count);
    }

    [Fact]
    public void NameChangesBeyondDisplayedPrefixRemainVisibleAndScopeDoesNotUseIdPrefixes()
    {
        var source = "{" + string.Join(',', Enumerable.Range(0, 100).Select(i => $"\"name{i}\":{i}")) + "}";
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "object/0/rename", ("at", FieldValue.Of(99L)), ("name", FieldValue.Of("lastName")));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(fixture.Document);
        var changed = query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["state"] = "modified" }, SortColumn = "within" });
        Assert.Equal(2, changed.Rows.Count);
        Assert.Contains(changed.Rows, row => row.Key == "value/0");
        var review = fixture.Session.GetChanges();
        Assert.True(review.Complete);
        Assert.Single(review.Changes);
        Assert.Single(query.Query("json-tree", new() { Filters = new Dictionary<string, string> { ["within"] = "value/1" } }).Rows);
    }

    private static string Text(TableRow row, string column) => Assert.IsType<TextNode>(row.Cells[column]).Text;
}
