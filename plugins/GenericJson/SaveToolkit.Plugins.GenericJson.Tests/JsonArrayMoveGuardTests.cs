using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonArrayMoveGuardTests
{
    private const string Source = "[[[1],2],[],{},3]";

    [Theory]
    [InlineData("array/0/move-to", "value/1", "value/1", 0, "itself or its descendant")]
    [InlineData("array/0/move-to", "value/1", "value/2", 0, "itself or its descendant")]
    [InlineData("array/1/move-to", "value/2", "value/6", 0, "Destination must be a JSON array")]
    [InlineData("array/1/move-to", "value/2", "value/7", 0, "Destination must be a JSON array")]
    [InlineData("array/6/move-to", "value/2", "value/5", 0, "expected JSON container type")]
    [InlineData("array/7/move-to", "value/2", "value/5", 0, "expected JSON container type")]
    [InlineData("array/1/move-to", "value/3", "value/5", 0, "direct entry")]
    [InlineData("array/1/move-to", "value/0", "value/5", 0, "direct entry")]
    [InlineData("array/1/move-to", "value/999", "value/5", 0, "source identity is unknown")]
    [InlineData("array/1/move-to", "value/2", "value/999", 0, "destination identity is unknown")]
    [InlineData("array/999/move-to", "value/2", "value/5", 0, "Container no longer exists")]
    [InlineData("array/1/move-to", "value/02", "value/5", 0, "source identity is unknown")]
    [InlineData("array/1/move-to", "value/2", "structure/5", 0, "destination identity is unknown")]
    [InlineData("array/1/move-to", "value/2", "value/5", -1, "outside the current bounds")]
    [InlineData("array/1/move-to", "value/2", "value/5", 1, "outside the current bounds")]
    [InlineData("array/1/move-to", "value/2", "value/1", 0, "different final index")]
    [InlineData("array/1/move-to", "value/2", "value/1", 2, "outside the current bounds")]
    public void InvalidMovesRejectAtomicallyWithClearDiagnostics(string action, string source, string destination, int at, string message)
    {
        using var fixture = new JsonSessionFixture(Source);
        var planner = (IActionPlanningDocument)fixture.Document;
        var parameters = JsonArrayMoveTests.Parameters(source, destination, at).ToDictionary(p => p.Name, p => p.Value);
        var plan = planner.PlanAction(action, parameters, new());
        Assert.Contains(message, Assert.Single(plan.Errors).Message, StringComparison.Ordinal);
        Assert.Empty(plan.Changes);
        var rejected = planner.ApplyActionPlan(action, parameters, new(), plan);
        Assert.False(rejected.Succeeded);
        Assert.Contains(message, Assert.Single(rejected.Errors).Message, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(Source), fixture.Bytes);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.Equal(0, fixture.Session.State.Revision);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("at")]
    public void MissingOrWronglyTypedParametersRejectForDirectCallers(string name)
    {
        using var fixture = new JsonSessionFixture(Source);
        var planner = (IActionPlanningDocument)fixture.Document;
        var parameters = JsonArrayMoveTests.Parameters("value/2", "value/5", 0).ToDictionary(p => p.Name, p => p.Value);
        parameters.Remove(name);
        Assert.NotEmpty(planner.PlanAction("array/1/move-to", parameters, new()).Errors);
        parameters[name] = FieldValue.Of(false);
        Assert.NotEmpty(planner.PlanAction("array/1/move-to", parameters, new()).Errors);
        Assert.Equal(Encoding.UTF8.GetBytes(Source), fixture.Bytes);
    }

    [Fact]
    public void RemovedSourceDestinationAndSourceArrayIdsStayStale()
    {
        using var fixture = new JsonSessionFixture(Source);
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "array/0/remove", ("at", FieldValue.Of(0L)));
        AssertRejected("array/0/move-to", "value/1", "value/5");
        AssertRejected("array/0/move-to", "value/5", "value/2");
        AssertRejected("array/1/move-to", "value/2", "value/5");

        void AssertRejected(string action, string source, string destination)
        {
            var before = fixture.Bytes;
            var preview = fixture.Session.PlanAction(action, JsonArrayEditingTests.Parameters(JsonArrayMoveTests.Parameters(source, destination, 0)),
                expectedRevision: fixture.Session.State.Revision);
            Assert.Null(preview.PlanId);
            Assert.NotEmpty(preview.Plan.Errors);
            Assert.Equal(before, fixture.Bytes);
        }
    }

    [Fact]
    public void MovePreviewCannotApplyAfterSourceOrDestinationChanged()
    {
        using var fixture = new JsonSessionFixture(Source);
        _ = fixture.Fields();
        var parameters = JsonArrayMoveTests.Parameters("value/2", "value/5", 0);
        var preview = fixture.Session.PlanAction("array/1/move-to", JsonArrayEditingTests.Parameters(parameters), expectedRevision: 0);
        Assert.NotNull(preview.PlanId);
        JsonArrayEditingTests.Apply(fixture, "array/5/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of("null")));
        var before = fixture.Bytes;
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
        Assert.Equal(before, fixture.Bytes);
        var planner = (IActionPlanningDocument)fixture.Document;
        var rejected = planner.ApplyActionPlan("array/1/move-to", parameters.ToDictionary(p => p.Name, p => p.Value), new(), preview.Plan);
        Assert.False(rejected.Succeeded);
        Assert.Equal("The preview no longer matches this document.", Assert.Single(rejected.Errors).Message);
        Assert.Equal(before, fixture.Bytes);
    }

    [Fact]
    public void DepthIsPreflightedAndLargeSubtreesNeedOnlyTwoParentReviews()
    {
        var deep = new string('[', JsonTokenTree.MaxDepth - 1) + "0" + new string(']', JsonTokenTree.MaxDepth - 1);
        AssertRejected("[" + deep + ",[]]", "array/0/move-to", "value/1", "value/65", "maximum depth");

        var many = "[" + string.Join(',', Enumerable.Repeat("null", JsonResetActions.MaxChanges)) + "]";
        using var fixture = new JsonSessionFixture("[" + many + ",[]]");
        var tree = JsonTokenTree.Parse("[" + many + ",[]]");
        var destination = tree.Nodes[0].Children[1].ValueId;
        // Reparenting an existing subtree reviews only the two parents, even above the scalar review bound.
        var plan = ((IActionPlanningDocument)fixture.Document).PlanAction("array/0/move-to",
            JsonArrayMoveTests.Parameters("value/1", destination, 0).ToDictionary(p => p.Name, p => p.Value), new());
        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.Changes.Count);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, true)]
    public void DestinationTriviaByteGrowthHonorsExactLimit(int spareBytes, bool accepted)
    {
        const string skeleton = "[[0],[1,\n  2]]";
        var original = skeleton + new string(' ', JsonTokenTree.MaxBytes - skeleton.Length - spareBytes);
        using var fixture = new JsonSessionFixture(original);
        var planner = (IActionPlanningDocument)fixture.Document;
        var parameters = JsonArrayMoveTests.Parameters("value/2", "value/3", 2).ToDictionary(p => p.Name, p => p.Value);
        var plan = planner.PlanAction("array/1/move-to", parameters, new());
        Assert.Equal(accepted, plan.Errors.Count == 0);
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        if (!accepted)
        {
            Assert.Contains("16 MiB", Assert.Single(plan.Errors).Message, StringComparison.Ordinal);
            Assert.False(planner.ApplyActionPlan("array/1/move-to", parameters, new(), plan).Succeeded);
            Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
            return;
        }
        var outcome = planner.ApplyActionPlan("array/1/move-to", parameters, new(), plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(JsonTokenTree.MaxBytes, fixture.Bytes.Length);
        var redo = fixture.Document.RestoreMemento(Assert.IsAssignableFrom<DocumentMemento>(outcome.Revert));
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        _ = fixture.Document.RestoreMemento(redo);
        Assert.Equal(JsonTokenTree.MaxBytes, fixture.Bytes.Length);
    }

    private static void AssertRejected(string original, string action, string source, string destination, string message)
    {
        using var fixture = new JsonSessionFixture(original);
        var plan = ((IActionPlanningDocument)fixture.Document).PlanAction(action,
            JsonArrayMoveTests.Parameters(source, destination, 0).ToDictionary(p => p.Name, p => p.Value), new());
        Assert.Contains(message, Assert.Single(plan.Errors).Message, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(original), fixture.Bytes);
        Assert.False(fixture.Session.State.CanUndo);
    }
}
