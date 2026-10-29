using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticCancellationTests : IDisposable
{
    private readonly CounterPlugin _plugin = new();
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open(SemanticQueryableDocument current, Func<SemanticQueryableDocument> original)
    {
        var workspace = _store.Create("cancel", [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(5)))]);
        var session = new DocumentSession("test", workspace.Set.AllDiskFiles.Single(), _plugin, current,
            openOriginal: () => original());
        _ = session.BuildView();
        return session;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledSnapshotStopsPagingAndRetryHasCompleteBaseline(bool cancelOriginal)
    {
        using var cancellation = new CancellationTokenSource();
        var cancel = true;
        var opens = 0;
        QueryableCollectionPage CancelPage(QueryableCollectionPage page)
        {
            if (cancel) cancellation.Cancel();
            return page;
        }
        var original = new SemanticQueryableDocument { TransformPage = cancelOriginal ? CancelPage : null };
        var current = new SemanticQueryableDocument { TransformPage = cancelOriginal ? null : CancelPage };
        current.SetQuantity(0, 250, 99);
        using var session = Open(current, () => { opens++; return original; });
        var state = session.State;
        Assert.Throws<OperationCanceledException>(() => session.GetChanges(cancellation.Token));
        Assert.Equal(state, session.State);
        Assert.Single(cancelOriginal ? original.Queries : current.Queries);
        cancel = false;
        var log = session.GetChanges();
        Assert.True(log.Complete);
        Assert.Single(log.Changes);
        Assert.Equal(2, opens); // Neither baseline nor partial current traversal is published on abort.
    }

    [Fact]
    public void CancelledCollectionDoesNotPublishItsRowBindings()
    {
        using var cancellation = new CancellationTokenSource();
        var current = new SemanticQueryableDocument { TransformPage = page => { cancellation.Cancel(); return page; } };
        using var session = Open(current, () => new());
        Assert.Throws<OperationCanceledException>(() => session.QueryCollection("inventory/0", new() { Limit = 1 }, 0, cancellation.Token));
        var edit = new IncomingEdit(SemanticQueryableDocument.ValueId(0, 0), JsonSerializer.SerializeToElement(7));
        Assert.False(session.ApplyEdits([edit]).Ok);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void CancelledRevertLeavesValuesAndHistoryUnchanged()
    {
        using var cancellation = new CancellationTokenSource();
        var cancel = false;
        var current = new SemanticQueryableDocument { TransformPage = page => { if (cancel) cancellation.Cancel(); return page; } };
        current.SetQuantity(0, 250, 99);
        using var session = Open(current, () => new());
        var log = session.GetChanges();
        var state = session.State;
        cancel = true;
        Assert.Throws<OperationCanceledException>(() => session.RevertChange(log.Changes.Single().ValueId, log.Revision, cancellation.Token));
        Assert.Equal(state, session.State);
        cancel = false;
        Assert.Equal("99", session.GetChanges().Changes.Single().CurrentValue.ToString());
    }
}
