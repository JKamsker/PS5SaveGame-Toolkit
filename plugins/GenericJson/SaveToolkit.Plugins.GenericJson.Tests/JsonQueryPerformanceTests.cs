using System.Text;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonQueryPerformanceTests
{
    [Theory]
    [InlineData("path", false)]
    [InlineData("path", true)]
    [InlineData("value", false)]
    [InlineData("value", true)]
    [InlineData("type", false)]
    [InlineData("type", true)]
    public void FilteredSortUsesTheCompleteArrayWithExactLexicalOrderAndIdentityTies(string column, bool descending)
    {
        const int count = 401;
        var source = "[" + string.Join(',', Enumerable.Range(0, count).Select(i => i % 7)) + "]";
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("data.json", Encoding.UTF8.GetBytes(source)));
        var query = (IQueryableDocument)document;
        var matching = Enumerable.Range(0, count).Where(i => (i % 7).ToString() == "3");
        string SortKey(int i) => column switch { "path" => $"$[{i}]", "value" => "3", _ => "number" };
        var ordered = descending ? matching.OrderByDescending(SortKey, StringComparer.Ordinal).ThenByDescending(i => i + 1)
            : matching.OrderBy(SortKey, StringComparer.Ordinal).ThenBy(i => i + 1);
        var expected = ordered.Select(i => $"value/{i + 1}").ToArray();
        var actual = new List<string>();
        for (var offset = 0; offset < expected.Length; offset += 11)
        {
            var page = query.Query("json-array-0", new() { Offset = offset, Limit = 11, SortColumn = column, Descending = descending,
                Filters = new Dictionary<string, string> { ["value"] = "3", ["type"] = "NUMBER" } });
            Assert.Equal(count, page.TotalCount);
            Assert.Equal(expected.Length, page.FilteredCount);
            actual.AddRange(page.Rows.Select(row => row.Key));
        }
        Assert.Equal(expected, actual);
        Assert.Empty(query.Query("json-array-0", new() { Offset = count + 1 }).Rows);
    }

    [Fact]
    public void PathPredicatesKeepEscapesDuplicateOccurrencesAndCombinedAncestorSegments()
    {
        var tree = JsonTokenTree.Parse("{\"a/b\":{\"x\\\"\":1,\"x\\\"\":2},\"a/b\":null}");
        Assert.Equal("$[\"a/b\"]#1[\"x\\u0022\"]#2", JsonPaths.ForNode(tree.Nodes[3]));
        Assert.True(JsonPaths.Contains(tree.Nodes[3], "#1[\"x\\u0022\"]#2"));
        Assert.False(JsonPaths.Contains(tree.Nodes[2], "#2"));
        var key = new string('k', 256);
        var deep = JsonTokenTree.Parse(string.Concat(Enumerable.Repeat("{\"" + key + "\":", 12)) + "1" + new string('}', 12));
        var last = deep.Nodes[^1];
        var path = JsonPaths.ForNode(last);
        Assert.EndsWith($"… ({last.ValueId})", path, StringComparison.Ordinal);
        Assert.True(JsonPaths.Contains(last, $"… ({last.ValueId})"));
        Assert.False(JsonPaths.Contains(last, "missing"));
    }

    [Fact]
    public void RenameAndMoveInvalidatePathsAndFriendlyLabelsAndUndoRestoresThem()
    {
        using var fixture = new JsonSessionFixture("[{\"MasterVolume\":1},{\"other\":2}]");
        _ = fixture.Fields();
        var query = (IQueryableDocument)fixture.Document;
        Assert.Single(query.Query("json-tree", new() { Text = "Master Volume" }).Rows);
        JsonArrayEditingTests.Apply(fixture, "object/1/rename", ("at", FieldValue.Of(0L)), ("name", FieldValue.Of("NewName")));
        Assert.Empty(query.Query("json-tree", new() { Text = "Master Volume" }).Rows);
        Assert.Single(query.Query("json-tree", new() { Text = "New Name" }).Rows);
        JsonArrayEditingTests.Apply(fixture, "array/0/move", ("from", FieldValue.Of(0L)), ("to", FieldValue.Of(1L)));
        var moved = Assert.Single(query.Query("json-tree", new() { Text = "New Name" }).Rows);
        Assert.StartsWith("$[1]", Assert.IsType<TextNode>(moved.Cells["path"]).Text, StringComparison.Ordinal);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Single(query.Query("json-tree", new() { Text = "Master Volume" }).Rows);
    }
}
