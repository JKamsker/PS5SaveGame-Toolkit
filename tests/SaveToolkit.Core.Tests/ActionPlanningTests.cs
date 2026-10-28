using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class ActionPlanningTests : IDisposable
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];
    private readonly PlanningPlugin _plugin = new();
    private readonly WorkspaceStore _store;

    public ActionPlanningTests() => _store = new WorkspaceStore(new PluginCatalog([_plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
    public void Dispose() => _store.Dispose();

    private DocumentSession Open(DocumentSessionOptions? options = null)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 0 }));
        var workspace = _store.Create("planning", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        var file = workspace.Set.AllDiskFiles.Single();
        var session = new DocumentSession("fixture", file, _plugin, _plugin.Open(file), options,
            () => new PlanningPlugin.PlanningDocument(file.RelativePath));
        _ = session.BuildView();
        return session;
    }

    [Fact]
    public void PlanApply_IsPureScopedAndOneUndoGroup_WithDerivedReview()
    {
        using var session = Open();
        var preview = session.PlanAction("refill", NoParameters, new ActionScope(["a", "b"]));
        Assert.NotNull(preview.PlanId);
        Assert.Equal(0, _plugin.LastOpened!.ApplyCount);
        Assert.False(session.State.CanUndo);
        Assert.Equal(3, preview.Plan.Changes.Count);
        Assert.True(preview.Plan.Changes.Single(c => c.ValueId == "total").Derived);
        Assert.Single(preview.Plan.SkippedRows);

        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(3, session.GetChanges().Changes.Count);
        Assert.True(session.GetChanges().Complete);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Empty(session.GetChanges().Changes);
        Assert.True(session.Redo().Ok);
        Assert.Equal(3, session.GetChanges().Changes.Count);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("undo")]
    [InlineData("redo")]
    [InlineData("structure")]
    public void InterveningMutation_RejectsStalePlan(string mutation)
    {
        using var session = Open();
        _ = session.GetChanges(); // Binds lazy fields.
        Assert.True(session.ApplyEdits([new IncomingEdit("item/a", JsonSerializer.SerializeToElement("3"))]).Ok);
        if (mutation == "redo") Assert.True(session.Undo().Ok);
        var preview = session.PlanAction("refill", NoParameters);
        switch (mutation)
        {
            case "edit": Assert.True(session.ApplyEdits([new IncomingEdit("item/a", JsonSerializer.SerializeToElement("5"))]).Ok); break;
            case "undo": Assert.True(session.Undo().Ok); break;
            case "redo": Assert.True(session.Redo().Ok); break;
            case "structure":
                var remove = session.PlanAction("remove", NoParameters, new ActionScope(["b"]));
                Assert.True(session.ApplyActionPlan(remove.PlanId!, remove.Revision).Ok);
                break;
        }

        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
    }

    [Fact]
    public void Apply_RevalidatesAndRejectsChangedEffectsOrErrors()
    {
        using var session = Open();
        var preview = session.PlanAction("refill", NoParameters);
        _plugin.LastOpened!.DifferentPlan = true;
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
        Assert.Equal(0, _plugin.LastOpened.ApplyCount);
        Assert.Empty(session.GetChanges().Changes);
        _plugin.LastOpened.DifferentPlan = false;
        preview = session.PlanAction("refill", NoParameters);
        _plugin.LastOpened.RejectApply = true;
        Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Empty(session.GetChanges().Changes);
        Assert.False(session.State.CanUndo);
    }

    [Fact]
    public void EmptyScopeIsNotDocumentScope_AndPlansAreSingleUse()
    {
        using var session = Open();
        var preview = session.PlanAction("refill", NoParameters, new ActionScope([]));
        Assert.Empty(preview.Plan.Changes);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.False(session.State.CanUndo);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
        Assert.Null(session.PlanAction("refill", NoParameters, new ActionScope(["missing"])).PlanId);
        Assert.False(session.InvokeAction("refill", NoParameters).Ok);
        Assert.True(session.InvokeAction("legacy", NoParameters).Ok);
    }

    [Fact]
    public void ReadOnlyDocument_CanPreviewButCannotApply()
    {
        using var session = Open();
        _plugin.LastOpened!.ReadOnly = true;
        var preview = session.PlanAction("refill", NoParameters);
        Assert.NotNull(preview.PlanId);
        Assert.False(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(0, _plugin.LastOpened.ApplyCount);
    }

    [Fact]
    public void RevertChange_RecomputesDerivedValues_AndIsUndoable()
    {
        using var session = Open();
        var preview = session.PlanAction("refill", NoParameters, new ActionScope(["a"]));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var log = session.GetChanges();
        Assert.False(log.Changes.Single(c => c.ValueId == "total").CanRevert);
        Assert.Equal(["section:Save", "section:items", "row:a"], log.Changes.Single(c => c.ValueId == "item/a").Path);
        Assert.True(session.RevertChange("item/a", log.Revision - 1).Stale);
        Assert.True(session.RevertChange("item/a", log.Revision).Ok);
        Assert.Empty(session.GetChanges().Changes);
        Assert.True(session.Undo().Ok);
        Assert.Equal(2, session.GetChanges().Changes.Count);
    }

    [Fact]
    public void StructuralPlan_ReviewAndUndoRestoreOriginalLazyFields()
    {
        using var session = Open();
        var preview = session.PlanAction("remove", NoParameters, new ActionScope(["a"]));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var change = session.GetChanges().Changes.Single(c => c.ValueId == "item/a");
        Assert.True(change.OriginalExists);
        Assert.False(change.CurrentExists);
        Assert.False(change.CanRevert);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.GetChanges().Changes);
        Assert.True(session.Redo().Ok);
        Assert.Equal(2, session.GetChanges().Changes.Count);
    }

    [Fact]
    public void OriginalBudget_IsExplicit_AndBaselineSurvivesHistoryEviction()
    {
        using (var capped = Open(new DocumentSessionOptions { MaxOriginalBytes = 1 }))
        {
            var preview = capped.PlanAction("refill", NoParameters);
            Assert.True(capped.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
            Assert.False(capped.GetChanges().Complete);
            Assert.NotEmpty(capped.GetChanges().Warnings);
        }

        using var session = Open(new DocumentSessionOptions { MaxHistory = 1 });
        var first = session.PlanAction("refill", NoParameters, new ActionScope(["a"]));
        Assert.True(session.ApplyActionPlan(first.PlanId!, first.Revision).Ok);
        var second = session.PlanAction("refill", NoParameters, new ActionScope(["b"]));
        Assert.True(session.ApplyActionPlan(second.PlanId!, second.Revision).Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal("2", session.GetChanges().Changes.Single(c => c.ValueId == "item/a").OriginalValue.ToString());
    }

    [Fact]
    public void RetainedScope_IsCopied_AndLatestPlanReplacesOlderTokens()
    {
        using var session = Open();
        string[] rows = ["a"];
        var preview = session.PlanAction("refill", NoParameters, new ActionScope(rows));
        rows[0] = "b";
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Contains(session.GetChanges().Changes, c => c.ValueId == "item/a");
        Assert.DoesNotContain(session.GetChanges().Changes, c => c.ValueId == "item/b");

        var old = session.PlanAction("refill", NoParameters);
        var latest = session.PlanAction("refill", NoParameters);
        Assert.NotEqual(old.PlanId, latest.PlanId);
        Assert.True(session.ApplyActionPlan(old.PlanId!, old.Revision).Stale);
    }

    [Fact]
    public void FailedStructuralApply_RestoresBindingsAndChangesNothing()
    {
        using var session = Open();
        var preview = session.PlanAction("remove", NoParameters, new ActionScope(["a"]));
        _plugin.LastOpened!.ThrowDuringApply = true;
        Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Empty(session.GetChanges().Changes);
        Assert.False(session.State.IsDirty);
        Assert.Equal(preview.Revision, session.State.Revision);
        Assert.True(session.ApplyEdits([new IncomingEdit("item/a", JsonSerializer.SerializeToElement("5"))]).Ok);
    }

    [Fact]
    public void Plan_ValidatesAndRetainsParameters_AndRejectsOversizedPreviews()
    {
        using var session = Open();
        var parameters = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("11") };
        var preview = session.PlanAction("refill", parameters, new ActionScope(["a"]));
        parameters["value"] = JsonSerializer.SerializeToElement("12");
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal("11", session.GetChanges().Changes.Single(c => c.ValueId == "item/a").CurrentValue.ToString());

        parameters["value"] = JsonSerializer.SerializeToElement("101");
        Assert.Null(session.PlanAction("refill", parameters).PlanId);
        _plugin.LastOpened!.OversizedPlan = true;
        var oversized = session.PlanAction("refill", NoParameters);
        Assert.Null(oversized.PlanId);
        Assert.Contains(oversized.Plan.Errors, error => error.Message.Contains("too large", StringComparison.Ordinal));
    }
}
