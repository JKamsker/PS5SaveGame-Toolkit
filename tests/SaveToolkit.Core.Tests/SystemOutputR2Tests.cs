using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class SystemOutputR2Tests
{
    private const string Root = "PPSA99997/sdimg_SAVE/";
    private const string SfoPath = Root + "sce_sys/param.sfo";

    [Fact]
    public void CleanOptedInDocumentDoesNotRequireOutputs()
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, true);
        using var store = Store(plugin);
        using var workspace = SystemOutputOwnershipTests.CreateWorkspace(store, plugin);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single(f => f.FileName == "slot.dat").Id, plugin.Info.Id);

        Assert.Empty(WorkspaceExporter.Plan(workspace, new()).Overrides);
        Assert.Null(DocumentDownloader.Capture(workspace, session));
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("additional")]
    [InlineData("duplicate")]
    [InlineData("case")]
    public void DirtyOptedInDocumentRequiresCompleteOutputs(string invalidOutput)
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, true);
        using var store = Store(plugin);
        using var workspace = SystemOutputOwnershipTests.CreateWorkspace(store, plugin);
        var session = SystemOutputOwnershipTests.OpenAndEdit(workspace, plugin);
        var before = session.State;
        var primary = session.File.ReadAllBytes();
        plugin.OutputTransform = outputs => invalidOutput switch
        {
            "primary" => outputs.Where(output => !string.Equals(output.RelativePath, session.File.RelativePath, StringComparison.OrdinalIgnoreCase)).ToArray(),
            "additional" => outputs.Where(output => !output.RelativePath.EndsWith("param.sfo", StringComparison.OrdinalIgnoreCase)).ToArray(),
            "duplicate" => [.. outputs, outputs[0]],
            "case" => [outputs[0], outputs[1], outputs[1] with { RelativePath = outputs[1].RelativePath.ToUpperInvariant() }],
            _ => outputs,
        };

        Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));
        Assert.Throws<ExportException>(() => DocumentDownloader.Capture(workspace, session));
        Assert.Equal(before, session.State);
        Assert.Equal(primary, session.File.ReadAllBytes());
    }

    [Fact]
    public void CompletePrimaryAndAdditionalOutputsAreExportedAndDownloaded()
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, true);
        using var store = Store(plugin);
        using var workspace = SystemOutputOwnershipTests.CreateWorkspace(store, plugin);
        var session = SystemOutputOwnershipTests.OpenAndEdit(workspace, plugin);

        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.ChangedFiles });
        Assert.Equal(2, plan.Overrides.Count);
        var download = Assert.IsType<FileOutput>(DocumentDownloader.Capture(workspace, session));
        Assert.Equal(session.File.RelativePath, download.RelativePath);
    }

    [Fact]
    public void ConflictingOwnerAndThrowingDisposePreservePrimaryFailure()
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, false);
        using var store = Store(plugin);
        using var workspace = store.Create("cleanup", [new ImportSource("dump.zip", () => new MemoryStream(
            TestZips.Build((Root + "first.dat", [1]), (Root + "second.dat", [2]), (SfoPath, CreateSfo("Old"))))) ]);
        var first = workspace.Set.AllDiskFiles.Single(file => file.FileName == "first.dat");
        var second = workspace.Set.AllDiskFiles.Single(file => file.FileName == "second.dat");
        var existing = workspace.Open(first.Id, plugin.Info.Id);
        plugin.ThrowOnDispose = true;

        var error = Assert.Throws<AggregateException>(() => workspace.Open(second.Id, plugin.Info.Id));

        Assert.Contains(error.InnerExceptions, failure => failure is InvalidOperationException
            && failure.Message.Contains(SfoPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(error.InnerExceptions, failure => failure is IOException
            && failure.Message.Contains("cleanup failure", StringComparison.OrdinalIgnoreCase));
        Assert.Same(existing, workspace.FindDocument(existing.Id));
        plugin.ThrowOnDispose = false;
        _ = existing.BuildView();
    }

    private static WorkspaceStore Store(IGamePlugin plugin) =>
        new(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });

    private static byte[] CreateSfo(string subtitle)
    {
        var bytes = SystemOutputOwnershipTests.CreateSfo(subtitle);
        return bytes;
    }
}
