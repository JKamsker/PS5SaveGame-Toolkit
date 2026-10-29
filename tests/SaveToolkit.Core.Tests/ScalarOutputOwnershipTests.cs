using SaveToolkit.Core.Export;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class ScalarOutputOwnershipTests
{
    [Fact]
    public void CleanRepeatedScalarKeepsCleanCapturedStateAndOriginalPrimaryAndSystemBytes()
    {
        using var fixture = new ScalarOutputFixture();
        var before = fixture.Session.State;
        var result = fixture.Edit(1);
        Assert.True(result.Ok);
        Assert.Empty(result.Changes);
        Assert.Equal(before, result.State);
        var capture = fixture.Session.CaptureOutputs(CancellationToken.None);
        Assert.False(capture.IsDirty);
        Assert.Equal(before.Revision, capture.Revision);
        Assert.Empty(capture.Outputs);
        Assert.Empty(fixture.Verify(capture));
        Assert.Empty(WorkspaceExporter.Plan(fixture.Workspace, new()).Overrides);
        Assert.Null(DocumentDownloader.Capture(fixture.Workspace, fixture.Session));
        Assert.Equal(new byte[] { 1 }, fixture.Source(ScalarOutputFixture.PrimaryPath).ReadAllBytes());
        Assert.Equal(SystemOutputOwnershipTests.CreateSfo("Old"), fixture.Source(ScalarOutputFixture.SystemPath).ReadAllBytes());
    }

    [Theory]
    [InlineData("noop")]
    [InlineData("edit")]
    [InlineData("undo")]
    public void DirtyCaptureRetainsItsOwnStatusRevisionAndCopiedPrimaryAndSystemBytes(string laterOperation)
    {
        using var fixture = new ScalarOutputFixture();
        Assert.True(fixture.Edit(2).Ok);
        var before = fixture.Session.State;
        Assert.Empty(fixture.Edit(2).Changes);
        Assert.Equal(before, fixture.Session.State);
        var borrowed = new List<byte[]>();
        fixture.Plugin.OutputTransform = outputs => outputs.Select(output =>
        {
            var bytes = output.Content.ToArray();
            borrowed.Add(bytes);
            return output with { Content = bytes };
        }).ToArray();
        var capture = fixture.Session.CaptureOutputs(CancellationToken.None);
        Assert.True(capture.IsDirty);
        Assert.Equal(before.Revision, capture.Revision);
        foreach (var bytes in borrowed) bytes.AsSpan().Clear();
        fixture.Plugin.OutputTransform = null;
        if (laterOperation == "undo") Assert.True(fixture.Session.Undo().Ok);
        else Assert.True(fixture.Edit(laterOperation == "edit" ? 3 : 2).Ok);
        fixture.AssertDirtyBytes(fixture.Verify(capture), 2);
        Assert.Equal(before.Revision, capture.Revision);
        Assert.Equal(laterOperation != "undo", fixture.Session.State.IsDirty);
    }

    [Fact]
    public void CleanCaptureRemainsCleanAfterALaterEffectiveEdit()
    {
        using var fixture = new ScalarOutputFixture();
        Assert.Empty(fixture.Edit(1).Changes);
        var capture = fixture.Session.CaptureOutputs(CancellationToken.None);
        Assert.True(fixture.Edit(2).Ok);
        Assert.True(fixture.Session.State.IsDirty);
        Assert.False(capture.IsDirty);
        Assert.Equal(0, capture.Revision);
        Assert.Empty(fixture.Verify(capture));
        fixture.AssertDirtyBytes(WorkspaceExporter.Plan(fixture.Workspace, new()).Overrides, 2);
        Assert.Equal(new byte[] { 2 }, Assert.IsType<FileOutput>(DocumentDownloader.Capture(fixture.Workspace, fixture.Session)).Content.ToArray());
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("system")]
    [InlineData("duplicate")]
    [InlineData("case-duplicate")]
    [InlineData("extra")]
    public void IncompleteOrInvalidOwnedOutputsCannotProveARepeatedScalarIneffective(string invalid)
    {
        using var fixture = new ScalarOutputFixture();
        fixture.Plugin.OutputTransform = outputs => invalid switch
        {
            "primary" => [outputs[1]],
            "system" => [outputs[0]],
            "duplicate" => [.. outputs, outputs[0]],
            "case-duplicate" => [.. outputs, outputs[1] with { RelativePath = outputs[1].RelativePath.ToUpperInvariant() }],
            "extra" => [.. outputs, new(ScalarOutputFixture.Root + "sibling.dat", new byte[] { 1 })],
            _ => throw new InvalidOperationException("Unknown fixture variant."),
        };
        var result = fixture.Edit(1);
        Assert.True(result.Ok);
        Assert.Single(result.Changes);
        Assert.True(result.State.IsDirty);
        Assert.Equal(1, result.State.Revision);
        Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(fixture.Workspace, new()));
        Assert.Throws<ExportException>(() => DocumentDownloader.Capture(fixture.Workspace, fixture.Session));
        Assert.Equal(result.State, fixture.Session.State);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void RepeatedScalarDoesNotBypassCleanOrDirtyOwnedSourceErrors(bool dirty, bool system, bool missing)
    {
        using var fixture = new ScalarOutputFixture();
        if (dirty) Assert.True(fixture.Edit(2).Ok);
        Assert.Empty(fixture.Edit(dirty ? 2 : 1).Changes);
        var before = fixture.Session.State;
        var path = system ? ScalarOutputFixture.SystemPath : ScalarOutputFixture.PrimaryPath;
        if (missing) SystemOutputOwnershipTests.Delete(fixture.Source(path));
        else SystemOutputOwnershipTests.Tamper(fixture.Source(path), system ? SystemOutputOwnershipTests.CreateSfo("Altered") : [9]);
        var export = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(fixture.Workspace, new() { Scope = ExportScope.Everything }));
        var download = Assert.Throws<ExportException>(() => DocumentDownloader.Capture(fixture.Workspace, fixture.Session));
        Assert.Contains(path, export.Message, StringComparison.Ordinal);
        Assert.Contains(path, download.Message, StringComparison.Ordinal);
        Assert.Equal(before, fixture.Session.State);
    }

    [Fact]
    public void CancellationDuringRepeatedProofLeavesDirtyOutputsHistoryAndRevisionUnchanged()
    {
        using var fixture = new ScalarOutputFixture();
        Assert.True(fixture.Edit(2).Ok);
        var before = fixture.Session.State;
        using var cancellation = new CancellationTokenSource();
        fixture.Plugin.OutputTransform = outputs => { cancellation.Cancel(); return outputs; };
        Assert.Throws<OperationCanceledException>(() => fixture.Session.ApplyEdits(
            [ScalarOutputFixture.Incoming(2)], before.Revision, cancellation.Token));
        fixture.Plugin.OutputTransform = null;
        Assert.Equal(before, fixture.Session.State);
        fixture.AssertDirtyBytes(WorkspaceExporter.Plan(fixture.Workspace, new()).Overrides, 2);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.Empty(fixture.Edit(1).Changes);
        Assert.True(fixture.Session.State.CanRedo);
        Assert.True(fixture.Session.Redo().Ok);
        fixture.AssertDirtyBytes(WorkspaceExporter.Plan(fixture.Workspace, new()).Overrides, 2);
    }
}
