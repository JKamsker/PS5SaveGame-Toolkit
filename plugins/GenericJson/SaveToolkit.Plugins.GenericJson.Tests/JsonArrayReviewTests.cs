using System.Text;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonArrayReviewTests
{
    [Fact]
    public void ReviewDetectsReorderEmptyContainerAndScalarExistenceSeparatelyFromNull()
    {
        using var fixture = new JsonSessionFixture("[[],null,{\"x\":1,\"x\":2}]");
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "array/0/move", ("from", FieldValue.Of(0L)), ("to", FieldValue.Of(2L)));
        var reordered = fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(reordered.Complete);
        Assert.Contains(reordered.Changes, c => c.ValueId == "structure/0" && !c.CanRevert);
        JsonArrayEditingTests.Apply(fixture, "array/0/remove", ("at", FieldValue.Of(0L)));
        var removed = fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken);
        var nullRemoval = Assert.Single(removed.Changes, c => c.ValueId == "value/2");
        Assert.True(nullRemoval.OriginalExists);
        Assert.False(nullRemoval.CurrentExists);
        Assert.True(nullRemoval.OriginalValue.IsNull);
        JsonArrayEditingTests.Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of("null")));
        var added = fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains(added.Changes, c => !c.OriginalExists && c.CurrentExists && c.CurrentValue.IsNull);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Empty(fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes);
    }

    [Fact]
    public void StalePreviewAndOldCollectionRevisionCannotApplyAfterStructure()
    {
        using var fixture = new JsonSessionFixture("[1,2]");
        _ = fixture.Fields();
        var preview = fixture.Session.PlanAction("array/0/remove", JsonArrayEditingTests.Parameters(("at", FieldValue.Of(0L))), expectedRevision: 0, scope: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.True(fixture.Edit("value/1", JsonEditingTests.Number("3")).Ok);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        JsonArrayEditingTests.Apply(fixture, "array/0/remove", ("at", FieldValue.Of(0L)));
        Assert.Throws<StaleViewException>(() => fixture.Session.QueryCollection("json-tree", new(), 0, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(fixture.Session.InvokeAction("array/0/remove", JsonArrayEditingTests.Parameters(("at", FieldValue.Of(0L))), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
    }

    [Fact]
    public void SearchOnlyValueHistoryReplaysAfterStructuralUndoWithoutReloadingPage()
    {
        const string source = "[ {\"a\":\"\\u0041\"}, 2 ]";
        using var fixture = new JsonSessionFixture(source);
        _ = fixture.Session.BuildView();
        var row = Assert.Single(fixture.Session.QueryCollection("json-tree", new() { Text = "a", Filters = new Dictionary<string, string> { ["type"] = "string" } }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        var field = Assert.IsType<FieldNode>(Assert.Single(Assert.IsType<SectionNode>(row.Cells["value"]).Children));
        Assert.True(fixture.Edit(field.ValueId, JsonEditingTests.Text("changed")).Ok);
        JsonArrayEditingTests.Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of("false")));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.True(fixture.Session.Redo().Ok);
    }

    [Fact]
    public void UndoBranchDoesNotRecycleInsertedIdentity()
    {
        using var fixture = new JsonSessionFixture("[]");
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of("1")));
        var abandoned = Assert.Single(fixture.Fields()).ValueId;
        Assert.True(fixture.Session.Undo().Ok);
        JsonArrayEditingTests.Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of("2")));
        Assert.NotEqual(abandoned, Assert.Single(fixture.Fields()).ValueId);
        Assert.True(fixture.Edit(abandoned, JsonEditingTests.Number("9")).Stale);
    }

    [Fact]
    public void InvalidDepthTemplateIsRejectedByPurePreview()
    {
        using var fixture = new JsonSessionFixture("[[]]");
        _ = fixture.Fields();
        var template = new string('[', 64) + "null" + new string(']', 64);
        var preview = fixture.Session.PlanAction("array/1/insert", JsonArrayEditingTests.Parameters(("at", FieldValue.Of(0L)), ("json", FieldValue.Of(template))), scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Equal("[[]]", Encoding.UTF8.GetString(fixture.Bytes));
    }

    [Fact]
    public void SamplesKeepOverlayFallbackHonestAfterRawStructuralEditAndUndo()
    {
        var file = SampleSaves.Load("PPSA04027").AllDiskFiles.First(f => f.FileName == "settings.json");
        using var document = new GenericJsonPlugin().Open(file);
        var fields = JsonEditingTests.Fields(document);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var data = Assert.Single(query.Query("json-tree", new() { Text = "[\"data\"]#1", Filters = new Dictionary<string, string> { ["type"] = "array" } }).Rows,
            row => Assert.IsType<TextNode>(row.Cells["path"]).Text == "$[\"data\"]#1");
        var action = data.Key.Replace("value/", "array/", StringComparison.Ordinal) + "/insert";
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { ["at"] = FieldValue.Of(0L), ["json"] = FieldValue.Of("null") };
        var plan = planner.PlanAction(action, parameters, new());
        Assert.Empty(plan.Errors);
        var outcome = planner.ApplyActionPlan(action, parameters, new(), plan);
        Assert.True(outcome.Succeeded);
        Assert.DoesNotContain(Assert.IsType<TabsNode>(document.BuildView()).Tabs, t => t.Title.StartsWith("Cyberpunk", StringComparison.Ordinal));
        _ = document.RestoreMemento(Assert.IsAssignableFrom<DocumentMemento>(outcome.Revert));
        Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
        Assert.NotEmpty(fields);
    }
}
