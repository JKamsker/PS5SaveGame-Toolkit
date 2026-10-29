using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class SystemOutputR3Tests
{
    private const string Root = "PPSA99997/sdimg_SAVE/";
    private const string SfoPath = Root + "sce_sys/param.sfo";

    [Fact]
    public void DeclaredOrdinaryOutputSetRejectsUnownedExtraGamePath()
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, true);
        using var store = Store(plugin);
        using var workspace = store.Create("exact outputs", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root + "slot.dat", [1]), (Root + "sibling.dat", [9]), (SfoPath,
                SystemOutputOwnershipTests.CreateSfo("Old"))))) ]);
        var session = SystemOutputOwnershipTests.OpenAndEdit(workspace, plugin);
        plugin.OutputTransform = outputs => [.. outputs, new(Root + "sibling.dat", new byte[] { 8 })];

        Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));
        Assert.Equal(new byte[] { 9 }, workspace.Set.AllDiskFiles.Single(file => file.FileName == "sibling.dat").ReadAllBytes());
        Assert.True(session.State.IsDirty);
    }

    [Theory]
    [InlineData(ExportScope.Everything)]
    [InlineData(ExportScope.ChangedDirectories)]
    public void CleanDeclaredOwnerIsFingerprintPreflightedByWorkspaceExport(ExportScope scope)
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, true);
        using var store = Store(plugin);
        using var workspace = CreateSameDirectoryWorkspace(store);
        var clean = workspace.Set.AllDiskFiles.Single(file => file.FileName == "slot.dat");
        var cleanSession = workspace.Open(clean.Id, plugin.Info.Id);
        var cleanSfo = workspace.Set.AllDiskFiles.Single(file => file.RelativePath.Equals(SfoPath, StringComparison.OrdinalIgnoreCase));
        if (scope == ExportScope.ChangedDirectories)
        {
            plugin.Declarations = [];
            plugin.WriteSystemOutput = false;
            var dirty = workspace.Set.AllDiskFiles.Single(file => file.FileName == "other.dat");
            var dirtySession = workspace.Open(dirty.Id, plugin.Info.Id);
            _ = dirtySession.BuildView();
            Assert.True(dirtySession.ApplyEdits([new("value", JsonSerializer.SerializeToElement(2))]).Ok);
        }
        SystemOutputOwnershipTests.Tamper(cleanSfo, SystemOutputOwnershipTests.CreateSfo("Changed"));

        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new() { Scope = scope }));

        Assert.Contains(SfoPath, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(cleanSession.State.IsDirty);
    }

    [Fact]
    public void PrimarySourceMutationAfterPluginReadIsRejectedBeforeBinding()
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, true);
        using var store = Store(plugin);
        using var workspace = SystemOutputOwnershipTests.CreateWorkspace(store, plugin);
        var primary = workspace.Set.AllDiskFiles.Single(file => file.FileName == "slot.dat");
        plugin.AfterOpen = () => SystemOutputOwnershipTests.Tamper(primary, [8]);

        var error = Assert.Throws<InvalidOperationException>(() => workspace.Open(primary.Id, plugin.Info.Id));

        Assert.Contains("changed while the document was opening", error.Message, StringComparison.Ordinal);
        Assert.Empty(workspace.Documents);
        Assert.True(plugin.LastOpenedDisposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOwnedSourceIsMappedToExportFailureBeforeOverrides(bool primary)
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, true);
        using var store = Store(plugin);
        using var workspace = SystemOutputOwnershipTests.CreateWorkspace(store, plugin);
        var session = SystemOutputOwnershipTests.OpenAndEdit(workspace, plugin);
        var source = primary
            ? session.File
            : workspace.Set.AllDiskFiles.Single(file => file.RelativePath.Equals(SfoPath, StringComparison.OrdinalIgnoreCase));
        SystemOutputOwnershipTests.Delete(source);

        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));

        Assert.Contains(source.RelativePath, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.State.IsDirty);
    }

    [Fact]
    public void InitialGroupVerificationCleanupPreservesOriginalFailure()
    {
        var plugin = new GroupPlugin { ThrowOnDispose = true };
        using var store = Store(plugin);
        using var workspace = SystemOutputOwnershipTests.CreateGroupWorkspace(store, plugin);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "character.dat");
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        plugin.RejectValidation = true;

        var error = Assert.Throws<AggregateException>(() => workspace.OpenGroup(offer.Token));

        Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message.Contains("cross-member invariant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(error.Flatten().InnerExceptions, failure => failure.Message.Contains("group cleanup failure", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(workspace.DocumentGroups);
    }

    [Fact]
    public void CandidateVerificationCleanupPreservesOriginalFailureAndDoesNotPublish()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        using var workspace = SystemOutputOwnershipTests.CreateGroupWorkspace(store, plugin);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "character.dat");
        var group = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId)).Token);
        _ = group.Session.BuildView();
        plugin.RejectValidation = true;
        plugin.ThrowOnDispose = true;

        var result = group.Session.ApplyEdits([
            new("character/balance", JsonSerializer.SerializeToElement(4))]);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, failure => failure.Message.Contains("cross-member invariant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, failure => failure.Message.Contains("group cleanup failure", StringComparison.OrdinalIgnoreCase));
        plugin.RejectValidation = false;
        plugin.ThrowOnDispose = false;
        _ = group.Session.BuildView();
        Assert.Single(workspace.DocumentGroups);
    }

    [Fact]
    public void RoleVerificationCleanupPreservesOriginalFailureAndDoesNotPublish()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        using var workspace = SystemOutputOwnershipTests.CreateGroupWorkspace(store, plugin);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "character.dat");
        var group = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId)).Token);
        _ = group.Session.BuildView();
        plugin.ThrowRoleBuildView = true;
        plugin.ThrowOnDispose = true;

        var result = group.Session.ApplyEdits([
            new("character/balance", JsonSerializer.SerializeToElement(4))]);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, failure => failure.Message.Contains("role view failure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, failure => failure.Message.Contains("role cleanup failure", StringComparison.OrdinalIgnoreCase));
        plugin.ThrowRoleBuildView = false;
        plugin.ThrowOnDispose = false;
        _ = group.Session.BuildView();
        Assert.Single(workspace.DocumentGroups);
    }

    private static WorkspaceStore Store(IGamePlugin plugin) =>
        new(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });

    private static Workspace CreateSameDirectoryWorkspace(WorkspaceStore store) =>
        store.Create("clean preflight", [new ImportSource("dump.zip", () => new MemoryStream(TestZips.Build(
            (Root + "slot.dat", [1]), (SfoPath, SystemOutputOwnershipTests.CreateSfo("Old")),
            (Root + "other.dat", [1])))) ]);
}
