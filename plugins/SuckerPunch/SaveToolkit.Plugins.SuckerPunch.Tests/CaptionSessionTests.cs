using SaveToolkit.Core.Export;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class CaptionSessionTests
{
    [Fact]
    public void MetadataOnlyEditIsOneWholeHistoryUnitWithExactUndoRedoAndNoOpRevision()
    {
        using var fixture = new CaptionWorkspace();
        var session = fixture.Open();
        _ = session.BuildView();
        var clean = session.State;
        var noOp = CaptionWorkspace.Plan(session, CaptionTestData.Original);
        Assert.Empty(noOp.Plan.Errors);
        Assert.Empty(noOp.Plan.Changes);
        Assert.True(session.ApplyActionPlan(noOp.PlanId!, noOp.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(clean, session.State);
        Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
        CaptionWorkspace.Apply(session, "Longer 😀 caption");
        var review = session.GetChanges(TestContext.Current.CancellationToken);
        Assert.Contains(review.Changes, change => change.ValueId == CaptionTestData.ValueId);
        var edited = Outputs(session);
        Assert.Equal(fixture.Primary, edited[CaptionTestData.Root + "dis.dat"]);
        var changed = edited[CaptionTestData.Root + CaptionTestData.SfoPath];
        Assert.NotEqual(fixture.Sfo, changed);
        Assert.Equal(2, edited.Count);
        Assert.True(session.State.CanUndo && session.State.IsDirty);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty || session.State.CanUndo);
        Assert.Empty(session.Serialize(TestContext.Current.CancellationToken));
        Assert.Contains(CaptionTestData.Original, session.Title, StringComparison.Ordinal);
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, Outputs(session)[CaptionTestData.Root + CaptionTestData.SfoPath]);
        CaptionWorkspace.Apply(session, "é");
        Assert.True(session.Undo().Ok);
        Assert.Equal(changed, Outputs(session)[CaptionTestData.Root + CaptionTestData.SfoPath]);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void HostCapturedOutputBuffersDoNotAliasDocumentStateOrLaterCaptures()
    {
        using var fixture = new CaptionWorkspace();
        var session = fixture.Open();
        _ = session.BuildView();
        CaptionWorkspace.Apply(session, "first");
        var captured = session.Serialize(TestContext.Current.CancellationToken);
        var sfo = captured.Single(output => output.RelativePath.EndsWith(CaptionTestData.SfoPath, StringComparison.Ordinal));
        var expected = sfo.Content.ToArray();
        CaptionWorkspace.Apply(session, "second");
        Assert.Equal(expected, sfo.Content.ToArray());
        WriteAssertions.FlipReturnedByte(sfo.Content);
        Assert.Equal("second", CaptionTestData.Text(Outputs(session)[CaptionTestData.Root + CaptionTestData.SfoPath]));
        Assert.True(session.Undo().Ok);
        Assert.Equal(expected, Outputs(session)[CaptionTestData.Root + CaptionTestData.SfoPath]);
    }

    [Fact]
    public void CanonicalCaptionDirectEditAndQuantityActionUseExistingHostAuthority()
    {
        using var fixture = new CaptionWorkspace();
        var session = fixture.Open();
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new(CaptionTestData.ValueId, System.Text.Json.JsonSerializer.SerializeToElement("Manual"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var quantity = session.PlanAction("inventory/set-quantity", new Dictionary<string, System.Text.Json.JsonElement>
        { ["quantity"] = System.Text.Json.JsonSerializer.SerializeToElement("3") }, scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(quantity.Plan.Errors);
        Assert.NotNull(quantity.PlanId);
        Assert.True(session.ApplyActionPlan(quantity.PlanId, quantity.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Undo().Ok);
        Assert.Equal("Manual", CaptionTestData.Text(Outputs(session)[CaptionTestData.Root + CaptionTestData.SfoPath]));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.True(session.Redo().Ok);
        Assert.True(session.Redo().Ok);
    }

    [Fact]
    public void StaleRevisionDoesNotChangeAnyBytes()
    {
        using var fixture = new CaptionWorkspace();
        var session = fixture.Open();
        _ = session.BuildView();
        var preview = CaptionWorkspace.Plan(session, "first");
        CaptionWorkspace.Apply(session, "second");
        var before = Outputs(session);
        var rejected = session.ApplyActionPlan(preview.PlanId!, preview.Revision, TestContext.Current.CancellationToken);
        Assert.True(rejected.Stale);
        Assert.Equal(before[CaptionTestData.Root + CaptionTestData.SfoPath], Outputs(session)[CaptionTestData.Root + CaptionTestData.SfoPath]);
    }

    [Theory]
    [InlineData(ExportScope.ChangedFiles, 2)]
    [InlineData(ExportScope.ChangedDirectories, 4)]
    [InlineData(ExportScope.Everything, 6)]
    public void EveryExportScopePreservesPrimaryOtherFilesAndReopensCaption(ExportScope scope, int count)
    {
        using var fixture = new CaptionWorkspace();
        var session = fixture.Open();
        _ = session.BuildView();
        CaptionWorkspace.Apply(session, "Night run α");
        var plan = WorkspaceExporter.Plan(fixture.Workspace, new() { Scope = scope }, TestContext.Current.CancellationToken);
        Assert.Equal(count, plan.Files.Count);
        Assert.Equal(2, plan.Overrides.Count);
        Assert.Equal(fixture.Primary, plan.Overrides[CaptionTestData.Root + "dis.dat"].ToArray());
        Assert.False(plan.Overrides.ContainsKey(CaptionTestData.Root + "other.bin"));
        Assert.False(plan.Overrides.ContainsKey(CaptionWorkspace.Other + CaptionTestData.SfoPath));
        var overlay = new OverlaySaveSet(fixture.Workspace.Set, plan.Overrides);
        Assert.Equal("Night run α", overlay.Directories.Single(directory => directory.Name == "CAPTION").ParamSfo!.SubTitle);
        using var reopened = new SuckerPunchPlugin().Open(((SaveToolkit.Sdk.Saves.ISaveSet)overlay).FindFile(CaptionTestData.Root + "dis.dat")!);
        _ = reopened.BuildView();
        Assert.Equal("Night run α", reopened.GetValue(CaptionTestData.ValueId).AsString());
        using var zip = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, zip, TestContext.Current.CancellationToken);
        var archive = zip.ToArray();
        using var imported = fixture.Store.Create("reimport", [new ImportSource("edited.zip", () => new MemoryStream(archive))], TestContext.Current.CancellationToken);
        var restored = imported.Open(imported.Set.AllDiskFiles.Single(file => file.RelativePath == CaptionTestData.Root + "dis.dat").Id, "suckerpunch");
        Assert.Contains("Night run α", restored.Title, StringComparison.Ordinal);
        var repeat = WorkspaceExporter.Plan(fixture.Workspace, new() { Scope = scope }, TestContext.Current.CancellationToken);
        Assert.Equal(plan.Files, repeat.Files);
        foreach (var file in plan.Included.Where(file => !plan.Overrides.ContainsKey(file.RelativePath)))
            Assert.Equal(file.ReadAllBytes(), imported.Set.FindAnyFile(file.RelativePath)!.ReadAllBytes());
    }

    [Theory]
    [InlineData(ExportScope.ChangedFiles, 0)]
    [InlineData(ExportScope.ChangedDirectories, 0)]
    [InlineData(ExportScope.Everything, 6)]
    public void CleanAndNoOpExportsHaveNoOverrides(ExportScope scope, int count)
    {
        using var fixture = new CaptionWorkspace();
        var session = fixture.Open();
        _ = session.BuildView();
        CaptionWorkspace.Apply(session, CaptionTestData.Original);
        var plan = WorkspaceExporter.Plan(fixture.Workspace, new() { Scope = scope }, TestContext.Current.CancellationToken);
        Assert.Equal(count, plan.Files.Count);
        Assert.Empty(plan.Overrides);
    }

    private static Dictionary<string, byte[]> Outputs(DocumentSession session) => session.Serialize(TestContext.Current.CancellationToken).ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
}
