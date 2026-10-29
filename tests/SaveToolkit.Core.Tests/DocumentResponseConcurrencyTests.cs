using System.Text.Json;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class DocumentResponseConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentDto_ReportsTheRevisionOfItsViewDuringConcurrentEditsOrHistory(bool history)
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new WorkspaceStore(new PluginCatalog([new CounterPlugin()]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("DTO consistency", [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(1)))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, "test.counter");
        _ = session.BuildView();
        if (history)
        {
            Assert.True(session.ApplyEdits([new("counter", JsonSerializer.SerializeToElement(2))]).Ok);
            Assert.True(session.Undo().Ok);
        }
        using var start = new Barrier(2);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var editing = Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10), ct));
            for (var revision = 1; !stop.IsCancellationRequested; revision++)
            {
                var value = revision % 2 == 0 ? 1 : 2;
                var result = history
                    ? revision % 2 == 0 ? session.Undo() : session.Redo()
                    : session.ApplyEdits([new("counter", JsonSerializer.SerializeToElement(value))]);
                Assert.True(result.Ok);
            }
        }, ct);
        try
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10), ct));
            for (var attempt = 0; attempt < 2_000; attempt++)
            {
                var response = session.ToDto(workspace);
                var counter = Assert.IsType<SectionNode>(response.View).Children.OfType<FieldNode>().Single(field => field.ValueId == "counter");
                Assert.Equal(response.State.Revision % 2 == 0 ? 1 : 2, counter.Value.AsInt32());
            }
        }
        finally
        {
            stop.Cancel();
            await editing;
        }
    }
}
