using System.Text;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonArrayMoveTests
{
    private const string Subtree = "{ \"\\u0061\" : 1.2300E+02, \"a\":-0, \"s\":\"\\u0041\\/\\uD83D\\uDE00\", \"huge\":1e999999999999999999999 }";
    private const string Prefix = "\uFEFF \r\n{ \"outside\":{\"dup\":1.00,\"dup\":\"\\u0042\"}, \"src\" : ";
    private const string Between = " ,\r\n \"dst\"\t: ";
    private const string Suffix = ", \"tail\": [ -0 ,\t\"\\u0043\" ] } \r\n";

    public static TheoryData<string, string, int, string, string> Regions => new()
    {
        { "[ " + Subtree + " ,\ttrue,\n null  ]", "[\r\n  7,\r\n  8\r\n]", 0,
            "[ true,\n null  ]", "[\r\n  " + Subtree + ",\r\n  7,\r\n  8\r\n]" },
        { "[ false ,\t" + Subtree + ",\n null  ]", "[\r\n  7,\r\n  8\r\n]", 1,
            "[ false,\n null  ]", "[\r\n  7,\r\n  " + Subtree + ",\r\n  8\r\n]" },
        { "[ false ,\tnull,\n " + Subtree + "  ]", "[\r\n  7,\r\n  8\r\n]", 2,
            "[ false ,\tnull  ]", "[\r\n  7,\r\n  8,\r\n  " + Subtree + "\r\n]" },
        { "[ " + Subtree + "  ]", "[\t ]", 0, "[   ]", "[\t " + Subtree + "]" },
        { "[ " + Subtree + "  ]", "[]", 0, "[   ]", "[" + Subtree + "]" },
    };

    [Theory]
    [MemberData(nameof(Regions))]
    public void MovePreservesEveryByteOutsideArrayRegionsAndEverySubtreeToken(
        string sourceRegion, string destinationRegion, int at, string expectedSource, string expectedDestination)
    {
        var original = Prefix + sourceRegion + Between + destinationRegion + Suffix;
        var expected = Encoding.UTF8.GetBytes(Prefix + expectedSource + Between + expectedDestination + Suffix);
        var tree = JsonTokenTree.Parse(original);
        var source = tree.Nodes.Single(n => n.Label == "src");
        var destination = tree.Nodes.Single(n => n.Label == "dst");
        var moving = source.Children.Single(n => n.IsContainer);
        using var fixture = new JsonSessionFixture(original);
        var fields = fixture.Fields().ToDictionary(f => f.ValueId, f => f.Value, StringComparer.Ordinal);
        var action = Action(source);
        var parameters = Parameters(moving.ValueId, destination.ValueId, at);
        var preview = fixture.Session.PlanAction(action, JsonArrayEditingTests.Parameters(parameters), expectedRevision: 0);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(new[] { JsonStructureActions.StructureId(source), JsonStructureActions.StructureId(destination) },
            preview.Plan.Changes.Select(c => c.ValueId));
        Assert.All(preview.Plan.Changes, c =>
        {
            Assert.True(c.OldExists);
            Assert.True(c.NewExists);
            Assert.NotEqual(c.OldValue, c.NewValue);
        });
        Assert.Contains(preview.Plan.Changes, c => c.Label.Contains("[\"dst\"]#1", StringComparison.Ordinal)
            && c.NewValue.AsString().Contains(moving.ValueId, StringComparison.Ordinal));
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(expected, fixture.Bytes);
        Assert.Equal(fields.OrderBy(p => p.Key, StringComparer.Ordinal),
            fixture.Fields().ToDictionary(f => f.ValueId, f => f.Value, StringComparer.Ordinal).OrderBy(p => p.Key, StringComparer.Ordinal));
        var review = fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(review.Complete);
        Assert.Equal(2, review.Changes.Count);
        using var reopened = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("data.json", fixture.Bytes));
        Assert.Equal(expected, reopened.Serialize()[0].Content.ToArray());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(expected, fixture.Bytes);
    }

    [Theory]
    [InlineData("de-AT")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void MixedValueAndMoveHistoryKeepsBindingsAndCurrentPaths(string culture)
    {
        using var cultureScope = new TestCulture(culture);
        const string original = "[[ {\"a\":\"\\u0041\"},-0 ],[true]]";
        using var fixture = new JsonSessionFixture(original);
        _ = fixture.Fields();
        Assert.True(fixture.Edit("value/3", JsonEditingTests.Text("before")).Ok);
        var beforeMove = fixture.Bytes;
        JsonArrayEditingTests.Apply(fixture, "array/1/move-to", Parameters("value/2", "value/5", 1));
        var afterMove = fixture.Bytes;
        var moved = fixture.Fields().Single(f => f.ValueId == "value/3");
        Assert.Contains("$[1][1][\"a\"]#1", moved.Description, StringComparison.Ordinal);
        Assert.True(fixture.Edit(moved.ValueId, JsonEditingTests.Text("after")).Ok);
        var afterValue = fixture.Bytes;
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(afterMove, fixture.Bytes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(beforeMove, fixture.Bytes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        for (var i = 0; i < 3; i++) Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(afterValue, fixture.Bytes);
    }

    [Theory]
    [InlineData("[[[1]],[]]", "value/2", "value/4", "[[],[[1]]]", false)]
    [InlineData("[[[1]],[]]", "value/3", "value/0", "[[[]],[],1]", false)]
    [InlineData("[[1,2,3],[]]", "value/2", "value/1", "[[2,1,3],[]]", true)]
    public void NestedAndSameArrayDestinationsUseLiveIdentities(string original, string sourceId, string destinationId, string expected, bool sameArray)
    {
        using var fixture = new JsonSessionFixture(original);
        _ = fixture.Fields();
        var source = JsonTokenTree.Parse(original).FindValue(sourceId)!;
        var destination = JsonTokenTree.Parse(original).FindValue(destinationId)!;
        var at = sameArray ? 1 : destination.Children.Count;
        JsonArrayEditingTests.Apply(fixture, Action(source.Parent!), Parameters(sourceId, destinationId, at));
        Assert.Equal(Encoding.UTF8.GetBytes(expected), fixture.Bytes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
    }

    internal static string Action(JsonValueToken source) => $"array/{source.Index}/move-to";

    internal static (string Name, FieldValue Value)[] Parameters(string source, string destination, int at) =>
        [("source", FieldValue.Of(source)), ("destination", FieldValue.Of(destination)), ("at", FieldValue.Of((long)at))];
}
