using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonArrayMoveIntegrationTests
{
    [Fact]
    public void TitleOverlayRematchesOnMoveUndoAndRedo()
    {
        const string original = "{\"version\":136,\"data\":[{\"group_name\":\"/test\",\"options\":[]}],\"other\":[null]}";
        var tree = JsonTokenTree.Parse(original);
        var source = tree.Nodes.Single(n => n.Label == "other");
        var destination = tree.Nodes.Single(n => n.Label == "data");
        using var fixture = new JsonSessionFixture(original, title: "PPSA04027", fileName: "settings.json");
        _ = fixture.Fields();
        Assert.Equal(4, Assert.IsType<TabsNode>(fixture.Session.BuildView()).Tabs.Count);
        JsonArrayEditingTests.Apply(fixture, JsonArrayMoveTests.Action(source),
            JsonArrayMoveTests.Parameters(source.Children[0].ValueId, destination.ValueId, 1));
        Assert.Equal(3, Assert.IsType<TabsNode>(fixture.Session.BuildView()).Tabs.Count);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(4, Assert.IsType<TabsNode>(fixture.Session.BuildView()).Tabs.Count);
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(3, Assert.IsType<TabsNode>(fixture.Session.BuildView()).Tabs.Count);
    }

    [Fact]
    public void ArrayCollectionsAndSubtreeSearchFollowLiveParentageAcrossHistory()
    {
        var original = "[[{\"x\":-0}," + string.Join(',', Enumerable.Repeat("null", JsonSearch.ArrayTableThreshold))
            + "],[" + string.Join(',', Enumerable.Repeat("true", JsonSearch.ArrayTableThreshold)) + "]]";
        var tree = JsonTokenTree.Parse(original);
        var source = tree.Nodes[0].Children[0];
        var destination = tree.Nodes[0].Children[1];
        var moving = source.Children[0];
        var sourceCollection = JsonSearch.ArrayPrefix + source.Index;
        var destinationCollection = JsonSearch.ArrayPrefix + destination.Index;
        using var fixture = new JsonSessionFixture(original);
        _ = fixture.Fields();
        var query = (IQueryableDocument)fixture.Document;
        Assert.Contains(query.Collections, c => c.Id == sourceCollection);
        Assert.DoesNotContain(query.Collections, c => c.Id == destinationCollection);
        JsonArrayEditingTests.Apply(fixture, JsonArrayMoveTests.Action(source),
            JsonArrayMoveTests.Parameters(moving.ValueId, destination.ValueId, 100));
        Assert.DoesNotContain(query.Collections, c => c.Id == sourceCollection);
        Assert.Contains(query.Collections, c => c.Id == destinationCollection);
        Assert.Throws<KeyNotFoundException>(() => query.Query(sourceCollection, new()));
        Assert.Equal(moving.ValueId, Assert.Single(query.Query(destinationCollection, new() { Offset = 100 }).Rows).Key);
        Assert.DoesNotContain(Within(source.ValueId), row => row.Key == moving.ValueId);
        Assert.Contains(Within(destination.ValueId), row => row.Key == moving.ValueId);
        Assert.Equal(new[] { moving.ValueId, moving.Children[0].ValueId }, Within(moving.ValueId).Select(row => row.Key));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Contains(query.Collections, c => c.Id == sourceCollection);
        Assert.DoesNotContain(query.Collections, c => c.Id == destinationCollection);
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(moving.ValueId, Assert.Single(query.Query(destinationCollection, new() { Offset = 100 }).Rows).Key);

        IReadOnlyList<TableRow> Within(string id) => query.Query(JsonSearch.CollectionId,
            new() { Limit = 200, Filters = new Dictionary<string, string> { ["within"] = id } }).Rows;
    }

    [Fact]
    public void InsertedSubtreesKeepAllocatedIdsAndExactTokensWhenMovedAgain()
    {
        const string original = "[[],[],[]]";
        const string inserted = "{ \"\\u0061\" : -0,\"a\":\"\\u0041\" }";
        using var fixture = new JsonSessionFixture(original);
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "array/1/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of(inserted)));
        var identities = fixture.Fields().Select(f => f.ValueId).ToArray();
        JsonArrayEditingTests.Apply(fixture, "array/1/move-to", JsonArrayMoveTests.Parameters("value/4", "value/2", 0));
        Assert.Equal(Encoding.UTF8.GetBytes("[[],[" + inserted + "],[]]"), fixture.Bytes);
        var parameters = JsonArrayMoveTests.Parameters("value/4", "value/3", 0).ToDictionary(p => p.Name, p => p.Value);
        var staleSource = ((IActionPlanningDocument)fixture.Document).PlanAction("array/1/move-to", parameters, new());
        Assert.Contains("direct entry", Assert.Single(staleSource.Errors).Message, StringComparison.Ordinal);
        JsonArrayEditingTests.Apply(fixture, "array/2/move-to", JsonArrayMoveTests.Parameters("value/4", "value/3", 0));
        var expected = Encoding.UTF8.GetBytes("[[],[],[" + inserted + "]]");
        Assert.Equal(expected, fixture.Bytes);
        Assert.Equal(identities, fixture.Fields().Select(f => f.ValueId));
        for (var i = 0; i < 3; i++) Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        for (var i = 0; i < 3; i++) Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(expected, fixture.Bytes);
        Assert.Equal(identities, fixture.Fields().Select(f => f.ValueId));
    }

    [Fact]
    public void MoveRejectsRowSelections()
    {
        using var fixture = new JsonSessionFixture("[[1],[2]]");
        var parameters = JsonArrayMoveTests.Parameters("value/2", "value/3", 1);
        var planner = (IActionPlanningDocument)fixture.Document;
        var plan = planner.PlanAction("array/1/move-to", parameters.ToDictionary(p => p.Name, p => p.Value), new(["value/2"]));
        Assert.Contains("not a row selection", Assert.Single(plan.Errors).Message, StringComparison.Ordinal);
        Assert.Equal("[[1],[2]]"u8.ToArray(), fixture.Bytes);
        Assert.False(fixture.Session.State.CanUndo);
    }
}
