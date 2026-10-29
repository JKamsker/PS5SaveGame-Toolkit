using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class RecursiveInventoryBindingTests
{
    private const string SlotsId = "live/0/0/slots";
    private const string InventoryId = "live/0/0/inventory";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RecursiveSlotsFollowEachContainerSwapThroughQueriesReviewHistoryAndReopen(int container)
    {
        var blob = RecursiveInventoryFixture.Blob();
        var original = RecursiveInventoryFixture.Resource(blob);
        var imported = new LsmfLiveSource(0, 0, blob, LsfReader.Read(original));
        AssertFixture(imported);
        var keys = imported.Slots.ToDictionary(slot => slot.Key, slot => slot.Slot, StringComparer.Ordinal);
        var pair = imported.Slots.Where(slot => slot.ContainerRow == container).ToArray();
        using var workspace = new SyntheticSaveWorkspace("Globals.lsf", original);
        using var document = Assert.IsType<BaldursGate3Document>(workspace.Plugin.Open(workspace.File));
        using var session = new DocumentSession("recursive-slot-bindings", workspace.File, workspace.Plugin, document);
        _ = session.BuildView();
        _ = session.Expand(imported.Prefix);
        QueryableCollectionPage Query(string id, QueryableCollectionQuery query) =>
            session.QueryCollection(id, query, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        AssertCurrentTables(Query, imported, keys);
        Assert.Empty(session.GetChanges().Changes);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());

        var scope = new ActionScope { RowKeys = pair.Select(slot => imported.Prefix + "/" + slot.Key).ToArray() };
        var preview = session.PlanAction(LsmfLiveActions.Swap, new Dictionary<string, JsonElement>(), scope,
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        keys[pair[0].Key] = pair[1].Slot;
        keys[pair[1].Key] = pair[0].Slot;
        AssertCurrentTables(Query, imported, keys);
        AssertReviewNavigation(session, document, Query, pair, imported.Prefix);

        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        var editedBlob = LsfReader.Read(edited).Attributes[0].Data.ToArray();
        Assert.Equal(blob.Length, editedBlob.Length);
        var allowed = pair.SelectMany(slot => Enumerable.Range(slot.KeyOffset, sizeof(ushort))).ToHashSet();
        var differences = Enumerable.Range(0, blob.Length).Where(offset => blob[offset] != editedBlob[offset]).ToArray();
        Assert.NotEmpty(differences);
        Assert.All(differences, offset => Assert.Contains(offset, allowed));
        Assert.Equal(RecursiveInventoryFixture.Resource(editedBlob), edited);
        using var reopened = Assert.IsType<BaldursGate3Document>(workspace.Plugin.Open(MemorySaveSet.SingleFile("Globals.lsf", edited)));
        AssertCurrentTables(reopened.Query, imported, keys);
        Assert.Equal(edited, Assert.Single(reopened.Serialize()).Content.ToArray());

        Assert.True(session.Undo().Ok);
        AssertCurrentTables(Query, imported, imported.Slots.ToDictionary(slot => slot.Key, slot => slot.Slot, StringComparer.Ordinal));
        Assert.Empty(session.GetChanges().Changes);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        AssertCurrentTables(Query, imported, keys);
        AssertReviewNavigation(session, document, Query, pair, imported.Prefix);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CurrentKeysDoNotMutateImportedGraphRecordsOrOriginalBytes()
    {
        var blob = RecursiveInventoryFixture.Blob();
        var original = blob.ToArray();
        var source = new LsmfLiveSource(0, 0, blob, LsfReader.Read(RecursiveInventoryFixture.Resource(blob)));
        AssertFixture(source);
        var graph = Assert.IsType<LsmfInventoryGraph>(source.Inventory);
        var entries = graph.Entries.ToArray();
        var slots = source.Slots.ToArray();
        var baseline = source.Capture();
        source.Swap(slots[0], slots[1]);
        Assert.Same(graph, source.Inventory);
        Assert.Equal(entries, graph.Entries);
        Assert.Equal(slots, source.Slots);
        Assert.Equal(original, blob);
        Assert.Equal(original, source.Access.Model.Bytes.ToArray());
        foreach (var entry in entries)
        {
            var slot = Assert.Single(slots, slot => slot.ContainerRow == entry.ContainerRow && slot.EntityRow == entry.ItemEntityRow);
            Assert.Equal(slot.Slot, entry.Slot);
            Assert.Equal(source.Slot(slot), source.Slot(entry));
        }
        Assert.NotEqual(entries[0].Slot, source.Slot(entries[0]));
        source.Restore(baseline);
        Assert.All(entries, entry => Assert.Equal(entry.Slot, source.Slot(entry)));
        Assert.Equal(original, source.Write());
    }

    private static void AssertFixture(LsmfLiveSource source)
    {
        Assert.Empty(source.Diagnostics);
        var party = Assert.Single(Assert.IsType<LsmfIdentityGraph>(source.Identity).Party);
        Assert.Equal(1, party.BodyEntityRow);
        var entries = Assert.IsType<LsmfInventoryGraph>(source.Inventory).Entries;
        Assert.Equal(6, entries.Count); // The nested back-edge terminates without losing its row.
        Assert.Equal([2, 0, 5, 4, 5, 6], entries.Select(entry => entry.ItemEntityRow));
        Assert.Equal(4, entries.Count(entry => entry.OwnerBodyEntityRow == 1));
        Assert.Equal(2, entries.Count(entry => entry.OwnerBodyEntityRow is null));
        Assert.Equal(2, entries.Count(entry => entry.IsAmbiguous));
        Assert.Equal("container/0/slot/0/container/1/slot/0", entries[1].Path);
    }

    private static void AssertCurrentTables(Func<string, QueryableCollectionQuery, QueryableCollectionPage> query,
        LsmfLiveSource imported, IReadOnlyDictionary<string, int> keys)
    {
        var entries = Assert.IsType<LsmfInventoryGraph>(imported.Inventory).Entries;
        var bindings = imported.Slots.ToDictionary(slot => (slot.ContainerRow, slot.EntityRow));
        int Current(LsmfInventoryEntry entry) => keys[bindings[(entry.ContainerRow, entry.ItemEntityRow)].Key];
        string RowKey(LsmfInventoryEntry entry) => imported.Prefix + "/" + entry.Path;
        var slots = query(SlotsId, new());
        var inventory = query(InventoryId, new());
        Assert.Equal(6, inventory.TotalCount);
        Assert.Equal(6, inventory.FilteredCount);
        Assert.Equal(0, inventory.HiddenEmptyCount);
        Assert.Equal(entries.Select(RowKey), inventory.Rows.Select(row => row.Key));
        foreach (var entry in entries)
        {
            var row = Assert.Single(inventory.Rows, row => row.Key == RowKey(entry));
            Assert.False(row.IsEmpty);
            Assert.Equal(Number(Current(entry)), Text(row, "slot")); // Same cell is used in row details.
            Assert.Equal(entry.Path, Text(row, "path"));
            Assert.Equal(entry.OwnerBodyEntityRow is { } owner ? Number(owner) : "Unknown", Text(row, "owner"));
            var native = bindings[(entry.ContainerRow, entry.ItemEntityRow)];
            var field = Assert.IsType<FieldNode>(Assert.Single(slots.Rows, row => row.Key == imported.Prefix + "/" + native.Key).Cells["slot"]);
            Assert.Equal(Current(entry), field.Value.AsInt32());
            var filtered = query(InventoryId, new() { Filters = new Dictionary<string, string>
                { ["container"] = Number(entry.ContainerRow), ["item"] = Number(entry.ItemEntityRow), ["slot"] = Number(Current(entry)) } });
            Assert.Equal(1, filtered.FilteredCount);
            Assert.Equal(RowKey(entry), Assert.Single(filtered.Rows).Key);
        }
        var search = query(InventoryId, new() { Text = "65535" });
        Assert.Equal(entries.Where(entry => Current(entry) == ushort.MaxValue).Select(RowKey), search.Rows.Select(row => row.Key));
        Assert.Equal(search.Rows.Count, search.FilteredCount);
        foreach (var descending in new[] { false, true })
        {
            var sorted = descending ? entries.OrderByDescending(Current) : entries.OrderBy(Current);
            var expected = sorted.ThenBy(RowKey, StringComparer.Ordinal).Select(RowKey).ToArray();
            var paged = new List<string>();
            for (var offset = 0; offset < entries.Count; offset += 2)
                paged.AddRange(query(InventoryId, new() { SortColumn = "slot", Descending = descending, Offset = offset, Limit = 2 }).Rows.Select(row => row.Key));
            Assert.Equal(expected, paged);
        }
    }

    private static void AssertReviewNavigation(DocumentSession session, BaldursGate3Document document,
        Func<string, QueryableCollectionQuery, QueryableCollectionPage> query, IReadOnlyList<LsmfSlot> pair, string prefix)
    {
        var log = session.GetChanges();
        Assert.True(log.Complete);
        Assert.Equal(2, log.Changes.Count);
        foreach (var slot in pair)
        {
            var id = prefix + "/" + slot.Key + "/key";
            var change = Assert.Single(log.Changes, change => change.ValueId == id);
            Assert.Equal(slot.Slot, change.OriginalValue.AsInt32());
            var target = Assert.IsType<SemanticCollectionTarget>(change.Target);
            Assert.Equal(SlotsId, target.CollectionId);
            Assert.Equal(prefix + "/" + slot.Key, target.RowKey);
            var row = Assert.Single(query(target.CollectionId, target.Query).Rows, row => row.Key == target.RowKey);
            var field = Assert.IsType<FieldNode>(row.Cells["slot"]);
            Assert.Equal(id, field.ValueId);
            Assert.Equal(change.CurrentValue, field.Value);
            Assert.Equal(field.Value, document.GetValue(id));
        }
    }

    private static string Text(TableRow row, string column) => Assert.IsType<TextNode>(row.Cells[column]).Text;
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
