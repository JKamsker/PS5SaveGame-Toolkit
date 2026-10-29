using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Tests;

public sealed class QueryableSemanticReviewTests : IDisposable
{
    private readonly CounterPlugin _plugin = new();
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open(SemanticQueryableDocument current, SemanticQueryableDocument original,
        DocumentSessionOptions? options = null)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)));
        var workspace = _store.Create("query review", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var session = new DocumentSession("review", workspace.Set.AllDiskFiles.Single(), _plugin, current, options, () => original);
        _ = session.BuildView();
        return session;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedRowsBeyondFirstPageAndEmptySlotsHaveLabelsAndTargets(bool cursor)
    {
        var current = new SemanticQueryableDocument { CursorPaging = cursor };
        var original = new SemanticQueryableDocument { CursorPaging = cursor };
        current.SetQuantity(0, 0, 7);
        current.SetQuantity(0, 250, 99);
        using var session = Open(current, original);
        var log = session.GetChanges();
        Assert.True(log.Complete);
        Assert.Empty(log.Warnings);
        Assert.Equal(2, log.Changes.Count);
        var change = log.Changes.Single(c => c.ValueId == SemanticQueryableDocument.ValueId(0, 250));
        Assert.Equal("250", change.OriginalValue.ToString());
        Assert.Equal("99", change.CurrentValue.ToString());
        Assert.Equal("Inventory / slot/250 / Quantity", change.Label);
        Assert.Equal(["section:Root", "section:inventory", "row:slot/250"], change.Path);
        Assert.True(change.CanRevert);
        var target = JsonSerializer.SerializeToElement(change).GetProperty("Target");
        Assert.Equal("inventory/0", target.GetProperty("CollectionId").GetString());
        Assert.Equal("slot/250", target.GetProperty("RowKey").GetString());
        Assert.Equal(250, target.GetProperty("Offset").GetInt32());
        Assert.All(current.Queries.Concat(original.Queries), q => Assert.True(q.Query.IncludeEmpty));
    }

    [Fact]
    public void IndividualRevertPreservesOtherRowsAndIsUndoable()
    {
        var current = new SemanticQueryableDocument();
        current.SetQuantity(0, 0, 7);
        current.SetQuantity(0, 250, 99);
        using var session = Open(current, new());
        var log = session.GetChanges();
        var id = SemanticQueryableDocument.ValueId(0, 250);
        Assert.True(session.RevertChange(id, log.Revision).Ok);
        Assert.Equal(SemanticQueryableDocument.ValueId(0, 0), Assert.Single(session.GetChanges().Changes).ValueId);
        Assert.True(session.Undo().Ok);
        Assert.Equal(2, session.GetChanges().Changes.Count);
        Assert.True(session.RevertChange(id, log.Revision).Stale);
    }

    [Fact]
    public void UnchangedRowsAreOmittedAndDuplicateTablesAreVisitedOnce()
    {
        var current = new SemanticQueryableDocument(3) { DuplicateTables = true };
        var original = new SemanticQueryableDocument(3) { DuplicateTables = true };
        using var session = Open(current, original);
        var log = session.GetChanges();
        Assert.True(log.Complete);
        Assert.Empty(log.Changes);
        Assert.Single(current.Queries);
        Assert.Single(original.Queries);
    }

    [Fact]
    public void StopsAtDeclaredTotalEvenWithTrailingCursor()
    {
        static QueryableCollectionPage Trailing(QueryableCollectionPage p) => p with { NextCursor = "ignored" };
        var current = new SemanticQueryableDocument(3) { TransformPage = Trailing };
        using var session = Open(current, new(3) { TransformPage = Trailing });
        Assert.True(session.GetChanges().Complete);
        Assert.Single(current.Queries);
    }

    [Fact]
    public void MemoryBudgetNeverClaimsCompleteReview()
    {
        var current = new SemanticQueryableDocument();
        current.SetQuantity(0, 250, 99);
        using var session = Open(current, new(), new DocumentSessionOptions { MaxOriginalBytes = 500 });
        var log = session.GetChanges();
        Assert.False(log.Complete);
        Assert.NotEmpty(log.Warnings);
        Assert.DoesNotContain(log.Changes, c => !c.OriginalExists || !c.CurrentExists);
    }
}
