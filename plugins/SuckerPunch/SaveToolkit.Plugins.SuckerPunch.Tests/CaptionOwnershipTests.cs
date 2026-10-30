using SaveToolkit.Core.Export;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class CaptionOwnershipTests
{
    [Fact]
    public void SameDirectoryDisAndMemoryCannotOwnSameMetadataButDifferentDirectoriesCan()
    {
        using var fixture = new CaptionWorkspace(secondary: true);
        _ = fixture.Open();
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Open(CaptionTestData.Root + "memory.dat"));
        Assert.Contains(CaptionTestData.SfoPath, error.Message, StringComparison.OrdinalIgnoreCase);
        var other = fixture.Open(CaptionWorkspace.Other + "dis.dat");
        _ = other.BuildView();
        CaptionWorkspace.Apply(other, "independent");
        var plan = WorkspaceExporter.Plan(fixture.Workspace, new(), TestContext.Current.CancellationToken);
        Assert.True(plan.Overrides.ContainsKey(CaptionWorkspace.Other + CaptionTestData.SfoPath));
        Assert.False(plan.Overrides.ContainsKey(CaptionTestData.Root + CaptionTestData.SfoPath));
    }

    [Fact]
    public void IdenticalDirectoryNameUnderAnotherTitleDoesNotBecomeATwin()
    {
        var files = new Dictionary<string, byte[]>
        {
            [CaptionTestData.Root + "dis.dat"] = new SyntheticSave(false).Bytes,
            [CaptionTestData.Root + CaptionTestData.SfoPath] = CaptionTestData.Sfo(),
            ["PPSA26344/sdimg_sce_bu_CAPTION/dis.dat"] = new SyntheticSave(true).Bytes,
            ["PPSA26344/sdimg_sce_bu_CAPTION/" + CaptionTestData.SfoPath] = CaptionTestData.Sfo(),
        };
        var zip = CaptionTestData.Zip(files);
        using var store = new SaveToolkit.Core.Sessions.WorkspaceStore(
            new SaveToolkit.Core.Plugins.PluginCatalog([new SuckerPunchPlugin()]), new()
            { DataRoot = Path.Combine(SaveToolkit.Testing.TestScratch.ProcessFolder, "caption-title-" + Guid.NewGuid().ToString("N")) });
        using var workspace = store.Create("title isolation", [new ImportSource("synthetic.zip", () => new MemoryStream(zip))], TestContext.Current.CancellationToken);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single(file => file.RelativePath == CaptionTestData.Root + "dis.dat").Id, "suckerpunch");
        _ = session.BuildView();
        CaptionWorkspace.Apply(session, "manual");
        var plan = WorkspaceExporter.Plan(workspace, new(), TestContext.Current.CancellationToken);
        Assert.All(plan.Overrides.Keys, path => Assert.StartsWith("PPSA02225/", path, StringComparison.Ordinal));
        Assert.Equal(CaptionTestData.Original, workspace.Set.Directories.Single(directory => directory.TitleId == "PPSA26344").ParamSfo!.SubTitle);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void TwinsOnlyMirrorIdenticalUnownedOriginalPaths(bool changedTwin, bool ownTwin, bool mirrored)
    {
        using var fixture = new CaptionWorkspace(twin: true, changedTwin: changedTwin);
        var session = fixture.Open();
        _ = session.BuildView();
        if (ownTwin) _ = fixture.Open(CaptionWorkspace.Twin + "dis.dat");
        CaptionWorkspace.Apply(session, "manual");
        var plan = WorkspaceExporter.Plan(fixture.Workspace, new(), TestContext.Current.CancellationToken);
        Assert.Equal(mirrored, plan.Overrides.ContainsKey(CaptionWorkspace.Twin + CaptionTestData.SfoPath));
        Assert.False(plan.Overrides.ContainsKey(CaptionWorkspace.Other + CaptionTestData.SfoPath));
        Assert.Equal(changedTwin ? "Other" : CaptionTestData.Original,
            fixture.Workspace.Set.Directories.Single(directory => directory.IsBackup).ParamSfo!.SubTitle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostFingerprintRejectsChangedSfoForCleanAndDirtyIncludedSessions(bool dirty)
    {
        using var fixture = new CaptionWorkspace();
        var session = fixture.Open();
        _ = session.BuildView();
        if (dirty) CaptionWorkspace.Apply(session, "manual");
        var sfo = fixture.Workspace.Set.Directories.Single(directory => directory.Name == "CAPTION").SystemFiles
            .OfType<DiskSaveFile>().Single(file => file.PathInDirectory == CaptionTestData.SfoPath);
        // Synthetic workspace storage only; never mutate private artifacts.
        using (var read = Assert.IsType<FileStream>(sfo.OpenRead()))
        {
            var path = read.Name;
            read.Dispose();
            File.WriteAllBytes(path, CaptionTestData.Sfo("native"));
        }
        Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(fixture.Workspace,
            new() { Scope = ExportScope.Everything }, TestContext.Current.CancellationToken));
        Assert.Equal(dirty, session.State.IsDirty);
    }
}
