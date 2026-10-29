using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonArrayEditingTests
{
    [Theory]
    [InlineData(0, "\uFEFF{\"a\":[ \"\\u0041\",\t1.2300e+00, -0 ],\"x\":true}\n", "\uFEFF{\"a\":[ 1.2300e+00, -0 ],\"x\":true}\n")]
    [InlineData(1, "[ 1 ,\t2,\n 3  ]", "[ 1,\n 3  ]")]
    [InlineData(2, "[ 1 ,\t2,\n 3  ]", "[ 1 ,\t2  ]")]
    public void RemoveFirstMiddleLastPreservesOtherTokensAndUndo(int index, string source, string expected)
    {
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Fields();
        var array = source.StartsWith('\uFEFF') ? "array/1/remove" : "array/0/remove";
        Apply(fixture, array, ("at", FieldValue.Of((long)index)));
        Assert.Equal(expected, Encoding.UTF8.GetString(fixture.Bytes));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(expected, Encoding.UTF8.GetString(fixture.Bytes));
    }

    [Fact]
    public void AdjacentAndAllRemovalsThenEmptyInsertionAreReversible()
    {
        const string source = "[ \"\\u0041\", -0, {\"x\":1,\"x\":2}  ]\n";
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Fields();
        for (var i = 0; i < 3; i++) Apply(fixture, "array/0/remove", ("at", FieldValue.Of(0L)));
        Assert.Equal("[   ]\n", Encoding.UTF8.GetString(fixture.Bytes));
        Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of("{\"new\":null,\"new\":\"\"}")));
        Assert.Contains("{\"new\":null,\"new\":\"\"}", Encoding.UTF8.GetString(fixture.Bytes), StringComparison.Ordinal);
        for (var i = 0; i < 4; i++) Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }

    [Fact]
    public void InsertMoveDuplicateKeepExistingIdsAndOriginalEscapesThroughMixedHistory()
    {
        const string source = "\uFEFF[ {\"x\":\"\\u0041\",\"x\":-0}, [1.2300E+02, null], true ]\r\n";
        using var fixture = new JsonSessionFixture(source);
        var initial = fixture.Fields();
        var escaped = initial[0];
        Assert.True(fixture.Edit(escaped.ValueId, JsonEditingTests.Text("changed")).Ok);
        var afterValue = fixture.Bytes;
        Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of("{\"added\":1e1000000}")));
        Assert.Equal(escaped.ValueId, fixture.Fields().Single(f => f.Label == "x" && f.Value.AsUnion().Type == "string").ValueId);
        Apply(fixture, "array/0/move", ("from", FieldValue.Of(2L)), ("to", FieldValue.Of(0L)));
        Apply(fixture, "array/4/duplicate", ("from", FieldValue.Of(0L)), ("at", FieldValue.Of(2L)));
        var currentIds = fixture.Fields().Select(f => f.ValueId).ToArray();
        Assert.Equal(currentIds.Length, currentIds.Distinct().Count());
        foreach (var original in initial) Assert.Contains(original.ValueId, currentIds);
        var finalBytes = fixture.Bytes;
        for (var i = 0; i < 3; i++) Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(afterValue, fixture.Bytes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        for (var i = 0; i < 4; i++) Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(finalBytes, fixture.Bytes);
        Assert.Equal(currentIds, fixture.Fields().Select(f => f.ValueId));
    }

    [Theory]
    [InlineData("insert", "{broken", 0)]
    [InlineData("insert", "[] []", 0)]
    [InlineData("insert", "null", 10)]
    [InlineData("remove", "null", -1)]
    [InlineData("remove", "null", 3)]
    public void InvalidOperationsNeverChangeBytesOrHistory(string operation, string json, int index)
    {
        using var fixture = new JsonSessionFixture("[1,2,3]");
        _ = fixture.Fields();
        var preview = fixture.Session.PlanAction($"array/0/{operation}", Parameters(("at", FieldValue.Of((long)index)), ("json", FieldValue.Of(json))), expectedRevision: 0, scope: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Equal("[1,2,3]", Encoding.UTF8.GetString(fixture.Bytes));
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void RemovedValueAndLazyIdsBecomeStaleAndUndoRestoresTheirIdentity()
    {
        using var fixture = new JsonSessionFixture("[[1],2]");
        var field = fixture.Fields()[0];
        var revision = fixture.Session.State.Revision;
        Apply(fixture, "array/0/remove", ("at", FieldValue.Of(0L)));
        Assert.True(fixture.Edit(field.ValueId, JsonEditingTests.Number("9"), revision).Stale);
        Assert.True(fixture.Edit(field.ValueId, JsonEditingTests.Number("9")).Stale);
        Assert.Throws<StaleViewException>(() => fixture.Session.Expand("children/1/0/1"));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(field.ValueId, fixture.Fields()[0].ValueId);
        Assert.True(fixture.Edit(field.ValueId, JsonEditingTests.Number("9")).Ok);
    }

    internal static void Apply(JsonSessionFixture fixture, string actionId, params (string Name, FieldValue Value)[] parameters)
    {
        var before = fixture.Bytes;
        var preview = fixture.Session.PlanAction(actionId, Parameters(parameters), expectedRevision: fixture.Session.State.Revision);
        Assert.True(preview.Plan.Errors.Count == 0, string.Join("; ", preview.Plan.Errors.Select(e => e.Message)));
        Assert.NotNull(preview.PlanId);
        Assert.Equal(before, fixture.Bytes);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.True(fixture.Session.State.CanUndo);
    }

    internal static Dictionary<string, JsonElement> Parameters(params (string Name, FieldValue Value)[] parameters) =>
        parameters.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Value), StringComparer.Ordinal);
}
