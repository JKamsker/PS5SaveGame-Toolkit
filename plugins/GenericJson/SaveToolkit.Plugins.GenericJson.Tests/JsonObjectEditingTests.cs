using System.Text;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonObjectEditingTests
{
    [Fact]
    public void RenamePreservesColonTriviaAndValueSpellingAndRestoringNameRestoresEscapedKey()
    {
        const string source = "\uFEFF{ \"\\u0061\\\"\" \t:\r\n 1.2300e+00, \"b\":\"\\u0041\" }\n";
        using var fixture = new JsonSessionFixture(source);
        var originalIds = fixture.Fields().Select(f => f.ValueId).ToArray();
        Apply(fixture, "rename", 0, ("name", FieldValue.Of("renamed")));
        Assert.Equal("\uFEFF{ \"renamed\" \t:\r\n 1.2300e+00, \"b\":\"\\u0041\" }\n", Encoding.UTF8.GetString(fixture.Bytes));
        Assert.Equal(originalIds, fixture.Fields().Select(f => f.ValueId));
        var review = fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(review.Complete);
        var nameChange = Assert.Single(review.Changes);
        Assert.Equal("structure/0", nameChange.ValueId);
        Assert.Contains("renamed", nameChange.CurrentValue.AsString(), StringComparison.Ordinal);
        Assert.False(nameChange.CanRevert);
        Apply(fixture, "rename", 0, ("name", FieldValue.Of("a\"")));
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.Empty(fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Contains("renamed", Encoding.UTF8.GetString(fixture.Bytes), StringComparison.Ordinal);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }

    [Theory]
    [InlineData(0, "{ \"a\" : 1,\t\"a\":-0,\n\"b\":[] }", "{ \"a\":-0,\n\"b\":[] }")]
    [InlineData(1, "{ \"a\" : 1,\t\"a\":-0,\n\"b\":[] }", "{ \"a\" : 1,\n\"b\":[] }")]
    [InlineData(2, "{ \"a\" : 1,\t\"a\":-0,\n\"b\":[] }", "{ \"a\" : 1,\t\"a\":-0 }")]
    public void RemovalAddressesOneDuplicateOccurrenceAndPreservesSurvivors(int at, string source, string expected)
    {
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Fields();
        Apply(fixture, "remove", at);
        Assert.Equal(expected, Encoding.UTF8.GetString(fixture.Bytes));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(expected, Encoding.UTF8.GetString(fixture.Bytes));
    }

    [Theory]
    [InlineData("{}", 0, "{\"new\":-0}")]
    [InlineData("{ }", 0, "{ \"new\":-0}")]
    [InlineData("{\"a\":1}", 0, "{\"new\":-0,\"a\":1}")]
    [InlineData("{\"a\":1}", 1, "{\"a\":1,\"new\":-0}")]
    [InlineData("{\"a\":1, \"b\":2}", 1, "{\"a\":1, \"new\":-0, \"b\":2}")]
    public void InsertUsesExplicitNameValueAndContainerSeparator(string source, int at, string expected)
    {
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Fields();
        Apply(fixture, "insert", at, ("name", FieldValue.Of("new")), ("json", FieldValue.Of("-0")));
        Assert.Equal(expected, Encoding.UTF8.GetString(fixture.Bytes));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }

    [Fact]
    public void UnicodeEmptyAndControlNamesRoundTripAndMixedHistoryRestoresExactBytes()
    {
        const string source = "{\"a\":{\"x\":\"\\u0041\"},\"b\":-0}";
        using var fixture = new JsonSessionFixture(source);
        var field = fixture.Fields().First(f => f.Label == "x");
        Apply(fixture, "rename", 0, ("name", FieldValue.Of("漢字\n\"😀")));
        Assert.True(fixture.Edit(field.ValueId, JsonEditingTests.Text("changed")).Ok);
        Apply(fixture, "insert", 0, ("name", FieldValue.Of("")), ("json", FieldValue.Of("[null,1e-999999]")));
        Apply(fixture, "remove", 1);
        Assert.True(fixture.Edit(field.ValueId, JsonEditingTests.Text("stale")).Stale);
        for (var i = 0; i < 4; i++) Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.Equal(field.ValueId, fixture.Fields().First(f => f.Label == "x").ValueId);
        for (var i = 0; i < 4; i++) Assert.True(fixture.Session.Redo().Ok);
    }

    [Theory]
    [InlineData("insert", "a", "null", 0)]
    [InlineData("rename", "a", "null", 1)]
    [InlineData("rename", "a", "null", 0)]
    [InlineData("insert", "new", "1,\"injected\":2", 0)]
    [InlineData("insert", "new", "{} {}", 0)]
    [InlineData("insert", "new", "null", 3)]
    [InlineData("remove", "new", "null", -1)]
    public void InvalidNamesTemplatesAndIndicesRejectWithoutMutation(string operation, string name, string json, int at)
    {
        using var fixture = new JsonSessionFixture("{\"a\":1,\"b\":2}");
        _ = fixture.Fields();
        var preview = fixture.Session.PlanAction($"object/0/{operation}", JsonArrayEditingTests.Parameters(
            ("at", FieldValue.Of((long)at)), ("name", FieldValue.Of(name)), ("json", FieldValue.Of(json))), scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.Equal("{\"a\":1,\"b\":2}", Encoding.UTF8.GetString(fixture.Bytes));
    }

    [Fact]
    public void RemovedContainersRetireActionsAndUndoBranchDoesNotRecycleObjectIds()
    {
        using var fixture = new JsonSessionFixture("{\"a\":{}}");
        _ = fixture.Fields();
        Apply(fixture, "remove", 0);
        var stale = fixture.Session.PlanAction("object/1/insert", JsonArrayEditingTests.Parameters(
            ("at", FieldValue.Of(0L)), ("name", FieldValue.Of("x")), ("json", FieldValue.Of("null"))), scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(stale.Plan.Errors);
        Apply(fixture, "insert", 0, ("name", FieldValue.Of("first")), ("json", FieldValue.Of("null")));
        var abandoned = Assert.Single(fixture.Fields()).ValueId;
        Assert.True(fixture.Session.Undo().Ok);
        Apply(fixture, "insert", 0, ("name", FieldValue.Of("second")), ("json", FieldValue.Of("null")));
        Assert.NotEqual(abandoned, Assert.Single(fixture.Fields()).ValueId);
        Assert.True(fixture.Edit(abandoned, JsonEditingTests.Text("stale")).Stale);
        Assert.Throws<StaleViewException>(() => fixture.Session.Expand("children/1/0/0"));
    }

    private static void Apply(JsonSessionFixture fixture, string operation, int index, params (string Name, FieldValue Value)[] parameters) =>
        JsonArrayEditingTests.Apply(fixture, $"object/0/{operation}", [("at", FieldValue.Of((long)index)), .. parameters]);
}
