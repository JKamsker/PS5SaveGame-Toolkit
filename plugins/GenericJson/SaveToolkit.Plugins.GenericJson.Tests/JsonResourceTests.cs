using System.Text;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonResourceTests
{
    [Fact]
    public void InvalidUnicodeTemplatesRejectWithoutReplacementOrMutation()
    {
        using var fixture = new JsonSessionFixture("[]");
        _ = fixture.Fields();
        var planner = Assert.IsAssignableFrom<SaveToolkit.Sdk.IActionPlanningDocument>(fixture.Document);
        foreach (var template in new[] { "\"\uD800\"", "\"\\uD800\"" })
        {
            var plan = planner.PlanAction("array/0/insert", new Dictionary<string, FieldValue>
                { ["at"] = FieldValue.Of(0L), ["json"] = FieldValue.Of(template) }, new());
            Assert.NotEmpty(plan.Errors);
            Assert.Equal("[]", Encoding.UTF8.GetString(fixture.Bytes));
        }
    }

    [Fact]
    public void OversizedEscapedScalarRejectsAtomicallyBeforeExport()
    {
        using var fixture = new JsonSessionFixture("[1,\"safe\"]");
        var fields = fixture.Fields();
        // Default JSON escaping produces six UTF-8 bytes per character, exceeding the 16 MiB file cap.
        var tooLarge = JsonEditingTests.Text(new string('é', 3 * 1024 * 1024));
        var outcome = fixture.Document.ApplyEdits([new(fields[0].ValueId, JsonEditingTests.Number("2")), new(fields[1].ValueId, tooLarge)]);
        Assert.False(outcome.Succeeded);
        Assert.Equal("[1,\"safe\"]", Encoding.UTF8.GetString(fixture.Bytes));
    }

    [Theory]
    [InlineData("[]", 0, "[1]")]
    [InlineData("[ ]", 0, "[ 1]")]
    [InlineData("[0]", 0, "[1,0]")]
    [InlineData("[0]", 1, "[0,1]")]
    [InlineData("[0, 2]", 1, "[0, 1, 2]")]
    [InlineData("[0, 2]", 2, "[0, 2, 1]")]
    public void InsertAtBoundariesAndEmptyContainersHasExplicitPunctuationPolicy(string source, int index, string expected)
    {
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "array/0/insert", ("at", FieldValue.Of((long)index)), ("json", FieldValue.Of("1")));
        Assert.Equal(expected, Encoding.UTF8.GetString(fixture.Bytes));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }

    [Fact]
    public void SameDocumentRefreshKeepsPreviouslyPublishedLazySectionsValid()
    {
        using var fixture = new JsonSessionFixture("[[1],[2]]");
        _ = fixture.Fields();
        _ = fixture.Session.BuildView();
        Assert.Single(fixture.Session.Expand("children/1/0/1"));
    }

    [Fact]
    public void SnapshotsCannotBeLoadedIntoAnotherDocument()
    {
        using var first = new JsonSessionFixture("[1]");
        using var second = new JsonSessionFixture("[2]");
        _ = first.Fields();
        var planner = Assert.IsAssignableFrom<SaveToolkit.Sdk.IActionPlanningDocument>(first.Document);
        var parameters = new Dictionary<string, FieldValue> { ["at"] = FieldValue.Of(0L) };
        var plan = planner.PlanAction("array/0/remove", parameters, new());
        var edit = planner.ApplyActionPlan("array/0/remove", parameters, new(), plan);
        Assert.NotNull(edit.Revert);
        Assert.Throws<ArgumentException>(() => second.Document.RestoreMemento(edit.Revert));
        Assert.Equal("[2]", Encoding.UTF8.GetString(second.Bytes));
    }
}
