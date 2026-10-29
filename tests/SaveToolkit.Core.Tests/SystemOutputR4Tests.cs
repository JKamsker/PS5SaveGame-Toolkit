using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class SystemOutputR4Tests
{
    [Theory]
    [InlineData("PPSA99997/sdimg_SAVE/slot.dat")]
    [InlineData("PPSA99997/sdimg_SAVE/sce_sys/param.sfo")]
    [InlineData("PPSA99990/sdimg_SAVE/character.dat")]
    public void UnauthorizedOwnedSourcesMapToPathSpecificOwnershipFailure(string path)
    {
        var source = new ThrowingReadFile(path, new UnauthorizedAccessException("denied"));

        var error = Assert.Throws<InvalidOperationException>(() => OwnedSourceReader.Hash(source, path, CancellationToken.None));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.IsType<UnauthorizedAccessException>(error.InnerException);
    }

    [Fact]
    public void MissingGroupMemberMapsToExportFailureBeforeOverrides()
    {
        var plugin = new GroupPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        using var workspace = SystemOutputOwnershipTests.CreateGroupWorkspace(store, plugin);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "character.dat");
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        _ = workspace.OpenGroup(offer.Token);
        var member = workspace.Set.AllDiskFiles.Single(file => file.FileName == "profile.dat");
        SystemOutputOwnershipTests.Delete(member);

        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.Everything }));

        Assert.Contains(member.RelativePath, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingReadFile(string relativePath, Exception failure) : ISaveFile
    {
        public string Id => relativePath;
        public ISaveDirectory Directory => null!;
        public string RelativePath => relativePath;
        public string PathInDirectory => relativePath[(relativePath.LastIndexOf('/') + 1)..];
        public string FileName => PathInDirectory;
        public long Length => 0;
        public Stream OpenRead() => throw failure;
        public byte[] ReadAllBytes() => throw failure;
        public byte[] ReadHeader(int count) => throw failure;
    }
}
