using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Api.Performance.Tests;

[Collection(ApiMeasurementCollection.Name)]
public sealed class ExportAllocationTests(ITestOutputHelper output)
{
    private const int PayloadBytes = 8 * 1024 * 1024;

    [Fact]
    public void ExportPlanKeepsOneSnapshotAndDoesNotCloneItAgain()
    {
        var plugin = new LiveBufferPlugin(new byte[PayloadBytes]);
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions
        { DataRoot = Path.Combine(Path.GetTempPath(), "export-allocation", Guid.NewGuid().ToString("N")) });
        var workspace = store.Create("large", [new ImportSource("large.bin", () => new MemoryStream(plugin.Bytes))]);
        var session = workspace.Open(workspace.Set.Directories.Single().Files.Single().Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new("value", System.Text.Json.JsonSerializer.SerializeToElement(1))]).Ok);
        var options = new ExportOptions { MirrorBackups = false };
        _ = WorkspaceExporter.Plan(workspace, options);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var plan = WorkspaceExporter.Plan(workspace, options);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        new ApiMeasurement(output, "8MiB-snapshot").Record("export-plan", allocated);
        Assert.True(allocated < PayloadBytes + 128 * 1024, $"Planning allocated {allocated:N0} B.");
        Assert.True(session.ApplyEdits([new("value", System.Text.Json.JsonSerializer.SerializeToElement(2))]).Ok);
        Assert.Equal(1, Assert.Single(plan.Overrides).Value.Span[0]);
        Assert.Equal(2, plugin.Bytes[0]);
    }
}
