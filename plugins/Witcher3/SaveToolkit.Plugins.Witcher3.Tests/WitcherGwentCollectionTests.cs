using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherGwentCollectionTests
{
    [Fact]
    public void CompleteCollectionSearchFiltersAndNumericSortReachUnloadedCards()
    {
        var cards = Enumerable.Range(0, 550).Select(i => new GwentCard(i, i) { Leader = i % 2 == 0 }).ToArray();
        var view = new GwentCollection(cards, null,
            card => new TableRow(GwentCollection.Key(card), new Dictionary<string, ViewNode>()));
        Assert.Equal(550, view.Build().TotalCount);
        Assert.Null(view.Collection.EmptyRows); // Zero copies is a named card record, not an unused slot.
        var match = view.Query(new() { Text = "549", Limit = 1 });
        Assert.Equal(550, match.TotalCount);
        Assert.Equal(1, match.FilteredCount);
        Assert.Equal("549", Assert.Single(match.Rows).Key);
        var page = view.Query(new() { Filters = new Dictionary<string, string> { ["kind"] = "Leader collection" },
            SortColumn = "copies", Descending = true, Offset = 1, Limit = 2 });
        Assert.Equal(275, page.FilteredCount);
        Assert.Equal(["546", "544"], page.Rows.Select(r => r.Key));
        Assert.Equal("549", Assert.Single(view.Query(new() { Offset = 549, Limit = 1 }).Rows).Key);
        var zero = Assert.Single(view.Query(new() { Filters = new Dictionary<string, string> { ["copies"] = "0" }, IncludeEmpty = false }).Rows);
        Assert.Equal("0", zero.Key);
        Assert.False(zero.IsEmpty);
        Assert.Equal(0, view.Query(new() { IncludeEmpty = false }).HiddenEmptyCount);
        Assert.Empty(view.Query(new() { Offset = int.MaxValue }).Rows);
        Assert.Equal(200, view.Query(new() { Limit = int.MaxValue }).Rows.Count);
        Assert.Single(view.Query(new() { Limit = 0 }).Rows);
        Assert.Throws<ArgumentException>(() => view.Query(new() { Cursor = "unsupported" }));
        Assert.Throws<ArgumentException>(() => view.Query(new() { SortColumn = "unsupported" }));
        Assert.Throws<ArgumentException>(() => view.Query(new() { Filters = new Dictionary<string, string> { ["unsupported"] = "x" } }));
    }

    [Fact]
    public void QueryDrivenCardEditChangesOnlyTheNativeCopyCountAndZeroCopiesRemainVisible()
    {
        var file = EnabledWriteFixture.Files()[0];
        var input = file.ReadAllBytes();
        var before = EnabledWriteFixture.Read(input);
        var plugin = new Witcher3Plugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA04018");
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("gwent-query", file, plugin, document, references: references);
        _ = session.BuildView();
        var page = session.QueryCollection(GwentCollection.CollectionId, new() {
            Text = references.Catalog("gwent")!.Find("108")!.Name,
            Filters = new Dictionary<string, string> { ["index"] = "108" },
        });
        var row = Assert.Single(page.Rows);
        var card = before.Features.Cards!.Single(c => GwentCollection.Key(c) == row.Key);
        var field = Assert.IsType<FieldNode>(row.Cells["copies"]);
        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming(field.ValueId, FieldValue.Of(0))]).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        var expectedPayload = before.Container.Payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(expectedPayload.AsSpan(card.CopiesOffset), 0);
        Assert.Equal(expectedPayload, WitcherContainer.Parse(edited).Payload);
        var zero = Assert.Single(session.QueryCollection(GwentCollection.CollectionId, new() {
            Filters = new Dictionary<string, string> { ["index"] = row.Key }, IncludeEmpty = false,
        }).Rows);
        Assert.Equal(row.Key, zero.Key);
        Assert.False(zero.IsEmpty);
        Assert.Equal(0, Assert.IsType<FieldNode>(zero.Cells["copies"]).Value.AsInt32());
        using var reopened = plugin.Open(SaveToolkit.Testing.MemorySaveSet.SingleFile("Manual.sav", edited, "PPSA04018"), references);
        _ = reopened.BuildView();
        Assert.Equal(0, Assert.IsType<FieldNode>(Assert.Single(((IQueryableDocument)reopened).Query(GwentCollection.CollectionId,
            new() { Filters = new Dictionary<string, string> { ["index"] = row.Key } }).Rows).Cells["copies"]).Value.AsInt32());
        Assert.True(session.Undo().Ok);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
