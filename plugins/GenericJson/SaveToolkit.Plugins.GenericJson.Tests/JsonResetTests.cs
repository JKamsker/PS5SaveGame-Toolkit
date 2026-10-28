using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonResetTests
{
    [Fact]
    public void PreviewedQueryScopeResetRestoresOnlyMatchingImportedScalarsAsOneUndoStep()
    {
        const string source = "[\"\\u0041\",-0,null]";
        using var fixture = new JsonSessionFixture(source);
        var fields = fixture.Fields();
        Assert.True(fixture.Edit(fields[0].ValueId, JsonEditingTests.Text("changed")).Ok);
        Assert.True(fixture.Edit(fields[1].ValueId, JsonEditingTests.Number("2")).Ok);
        Assert.True(fixture.Edit(fields[2].ValueId, JsonEditingTests.Text("changed")).Ok);
        var scope = new ActionScope
        {
            QueryScope = new("json-tree", new QueryableCollectionQuery { Text = "changed" }, fixture.Session.State.Revision),
        };
        var preview = fixture.Session.PlanAction("reset-imported", JsonArrayEditingTests.Parameters(), scope, fixture.Session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Contains(preview.Plan.Changes, c => c.Label.Contains("\"changed\" → null", StringComparison.Ordinal));
        Assert.Contains(preview.Plan.Changes, c => c.Label.Contains("\"changed\" → \"A\"", StringComparison.Ordinal));
        var before = fixture.Bytes;
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal("[\"\\u0041\",2,null]", Encoding.UTF8.GetString(fixture.Bytes));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(before, fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        var all = fixture.Session.PlanAction("reset-imported", JsonArrayEditingTests.Parameters(), expectedRevision: fixture.Session.State.Revision);
        Assert.NotNull(all.PlanId);
        Assert.True(fixture.Session.ApplyActionPlan(all.PlanId, all.Revision).Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.Empty(fixture.Session.GetChanges().Changes);
    }

    [Fact]
    public void NewEntriesAreSkippedInsteadOfBeingInventedAsImportedValues()
    {
        using var fixture = new JsonSessionFixture("[]");
        _ = fixture.Fields();
        JsonArrayEditingTests.Apply(fixture, "array/0/insert", ("at", FieldValue.Of(0L)), ("json", FieldValue.Of("1")));
        var inserted = fixture.Fields()[0];
        Assert.True(fixture.Edit(inserted.ValueId, JsonEditingTests.Number("2")).Ok);
        var preview = fixture.Session.PlanAction("reset-imported", JsonArrayEditingTests.Parameters(), new([inserted.ValueId]), fixture.Session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Changes);
        Assert.Single(preview.Plan.SkippedRows);
    }
}
