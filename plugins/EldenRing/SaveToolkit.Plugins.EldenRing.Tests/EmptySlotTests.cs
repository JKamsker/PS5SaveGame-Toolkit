using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class EmptySlotTests
{
    private const string HeldId = "slots/0/inventory/held";
    private const string StorageId = "slots/0/inventory/storage";
    private const string RefillId = "slots/0/inventory/refill";
    private const int HeldCapacity = SemanticLayout.HeldCommonCapacity + SemanticLayout.HeldKeyCapacity;
    private const int StorageCapacity = SemanticLayout.StorageCommonCapacity + SemanticLayout.StorageKeyCapacity;
    private static readonly Dictionary<string, JsonElement> NoParameters = [];

    [Fact]
    public void ViewPublishesOneTablePerInventoryAndMarksInactiveProfiles()
    {
        var fixture = new SemanticFixture();
        using var document = fixture.Save.Open();
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var inventory = root.Tabs.Single(tab => tab.Title == "Inventory / storage");
        var tables = inventory.Children.Cast<QueryableTableNode>().ToArray();
        Assert.Equal([HeldId, StorageId], tables.Select(table => table.CollectionId));
        foreach (var table in tables)
        {
            var metadata = ((IQueryableDocument)document).Collections.Single(collection => collection.Id == table.CollectionId);
            Assert.Equal(metadata.EmptyRows, table.EmptyRows);
            Assert.Equal("item", table.EmptyRows!.LabelColumnKey);
            Assert.True(table.EmptyRows.HideByDefault);
            Assert.Equal(table.CollectionId == HeldId ? HeldCapacity : StorageCapacity, table.TotalCount);
        }
        var profiles = Assert.Single(root.Tabs.Single(tab => tab.Title == "Profiles").Children.OfType<TableNode>());
        Assert.Equal("name", profiles.EmptyRows!.LabelColumnKey);
        Assert.True(profiles.EmptyRows.HideByDefault);
        Assert.Equal(SyntheticSave.SlotCount, profiles.Rows.Count);
        Assert.Equal("slot/0", Assert.Single(profiles.Rows, row => !row.IsEmpty).Key);
        Assert.All(profiles.Rows.Skip(1), row => Assert.True(row.IsEmpty));
        // Neither the inventory shells nor their character views introduce slot-range sections.
        var serialized = JsonSerializer.Serialize<ViewNode>(root)
            + JsonSerializer.Serialize(document.ExpandNode("characters/0"));
        Assert.DoesNotMatch(@"Slots \d+[–-]\d+", serialized.Replace("\\u2013", "–", StringComparison.Ordinal));
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Theory]
    [InlineData(HeldId, HeldCapacity, SemanticLayout.HeldKeyCapacity)]
    [InlineData(StorageId, StorageCapacity, SemanticLayout.StorageKeyCapacity)]
    public void EmptyInventoriesHideByDefaultAndCountOnlyMatchingVacancies(string id, int capacity, int keyCapacity)
    {
        var fixture = new SemanticFixture();
        using var document = fixture.Save.Open();
        var queryable = (IQueryableDocument)document;
        var hidden = queryable.Query(id, new());
        Assert.Equal(capacity, hidden.TotalCount);
        Assert.Equal(0, hidden.FilteredCount);
        Assert.Equal(capacity, hidden.HiddenEmptyCount);
        Assert.Empty(hidden.Rows);
        var explicitHidden = queryable.Query(id, new() { IncludeEmpty = false });
        Assert.Equal(hidden.FilteredCount, explicitHidden.FilteredCount);
        Assert.Equal(hidden.HiddenEmptyCount, explicitHidden.HiddenEmptyCount);
        Assert.Empty(explicitHidden.Rows);

        var query = new QueryableCollectionQuery { Text = "Empty slot", Filters = new Dictionary<string, string> { ["group"] = "key" } };
        var filtered = queryable.Query(id, query);
        Assert.Equal(0, filtered.FilteredCount);
        Assert.Equal(keyCapacity, filtered.HiddenEmptyCount);
        var shown = queryable.Query(id, query with { IncludeEmpty = true, Limit = 1 });
        Assert.Equal(capacity, shown.TotalCount);
        Assert.Equal(keyCapacity, shown.FilteredCount);
        Assert.Equal(0, shown.HiddenEmptyCount);
        var row = Assert.Single(shown.Rows);
        Assert.True(row.IsEmpty);
        var item = Assert.IsType<FieldNode>(row.Cells["item"]);
        Assert.Equal("Empty slot", item.Label);
        Assert.Equal("handle:0x00000000", item.Value.AsString());
        Assert.Equal(item.Value, document.GetValue(item.ValueId));
        Assert.Equal(0U, Assert.IsType<FieldNode>(row.Cells["quantity"]).Value.AsUInt32());
        Assert.Equal("0x00000000", Assert.IsType<TextNode>(row.Cells["handle"]).Text);
        Assert.Empty(queryable.Query(id, query with { Text = "missing item" }).Rows);
        Assert.Equal(0, queryable.Query(id, query with { Text = "missing item" }).HiddenEmptyCount);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Fact]
    public void OnlyAllZeroRecordsAreEmptyAndUnknownOrZeroQuantityResidueRemainsVisible()
    {
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 127, uint.MaxValue, 0, 0);
        fixture.Row("held", "common", 128, 0xB00050F0, 0, 9);
        fixture.Row("held", "common", 129, 0, 1, 0);
        fixture.Row("held", "common", 130, 0, 0, 1);
        fixture.Row("held", "common", 131, 0x8001FFFF, 1, 2);
        using var document = fixture.Save.Open();
        var queryable = (IQueryableDocument)document;
        var page = queryable.Query(HeldId, new() { SortColumn = "quantity", Limit = 2 });
        Assert.Equal(5, page.FilteredCount);
        Assert.Equal(HeldCapacity - 5, page.HiddenEmptyCount);
        Assert.All(page.Rows, row => Assert.False(row.IsEmpty));
        var allVisible = queryable.Query(HeldId, new() { Limit = 10 });
        Assert.Equal(5, allVisible.Rows.Count);
        Assert.All(allVisible.Rows, row => Assert.False(row.IsEmpty));
        Assert.Contains(allVisible.Rows, row => Assert.IsType<FieldNode>(row.Cells["item"]).Value.AsString() == "handle:0xFFFFFFFF");
        var unknown = queryable.Query(HeldId, new() { Text = "handle:0xFFFFFFFF" });
        Assert.Single(unknown.Rows);
        Assert.Equal(0, unknown.HiddenEmptyCount);
        var second = queryable.Query(HeldId, new() { SortColumn = "quantity", Limit = 2, Offset = 2 });
        Assert.Empty(page.Rows.Select(row => row.Key).Intersect(second.Rows.Select(row => row.Key)));
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Theory]
    [InlineData("held", HeldId, HeldCapacity)]
    [InlineData("storage", StorageId, StorageCapacity)]
    public void EverySlotKeepsItsIdentityAcrossPagingVisibilityEditingAndUndo(string container, string id, int capacity)
    {
        const int record = 1800;
        var fixture = new SemanticFixture();
        fixture.Row(container, "common", record, 0xB00050F0, 1, 77);
        using var disk = new SyntheticDiskSave(fixture.Save.Bytes);
        using var session = WriteTestSupport.Session(disk.File, out var document);
        var queryable = (IQueryableDocument)document;
        var visible = Assert.Single(session.QueryCollection(id, new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        Assert.False(visible.IsEmpty);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < capacity; offset += 137)
        {
            var page = queryable.Query(id, new() { IncludeEmpty = true, Offset = offset, Limit = 137, SortColumn = "quantity" });
            Assert.Equal(capacity, page.FilteredCount);
            Assert.Equal(0, page.HiddenEmptyCount);
            foreach (var row in page.Rows) Assert.True(identities.Add(row.Key));
        }
        Assert.Equal(capacity, identities.Count);
        var entries = SemanticReader.Read(fixture.Save.Bytes, EldenRingParser.Parse(fixture.Save.Bytes).Slots[0])
            .Containers.Single(inventory => inventory.Id == container).Entries;
        Assert.Equal(entries.Select(entry => entry.Key(0)).Order(), identities.Order());
        var entry = entries.Single(entry => !entry.IsEmpty);
        Assert.Equal(record, entry.Record);
        Assert.Equal(entry.Key(0), visible.Key);
        Assert.Equal(visible.Key, Assert.Single(queryable.Query(id, new() { IncludeEmpty = true, Text = "0x400050F0" }).Rows).Key);
        var quantity = Assert.IsType<FieldNode>(visible.Cells["quantity"]);
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming(quantity.ValueId, 2)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        WriteTestSupport.OnlyRanges(fixture.Save.Bytes, WriteTestSupport.Bytes(document), (entry.Offset + sizeof(uint), sizeof(uint)));
        Assert.Equal(2U, EldenRingParser.ReadUInt32(WriteTestSupport.Bytes(document), entry.Offset + sizeof(uint)));
        Assert.Equal(visible.Key, Assert.Single(session.QueryCollection(id, new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows).Key);
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Fact]
    public void SelectAllMatchingExcludesHiddenSlotsAcrossPagesAndHonorsExclusionsAndUndo()
    {
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 7, 0xB00050F0, 1, 1);
        fixture.Row("held", "common", 1800, 0xB0002774, 1, 2);
        fixture.Row("held", "key", 3, 0xB0002198, 1, 3);
        using var disk = new SyntheticDiskSave(fixture.Save.Bytes);
        using var session = WriteTestSupport.Session(disk.File, out var document);
        _ = session.QueryCollection(HeldId, new() { Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken); // Publish the table-cell F6 action.
        ActionScope Scope(QueryableCollectionQuery query, params string[] exclusions) => new()
        {
            QueryScope = new(HeldId, query, session.State.Revision) { ExcludedRowKeys = exclusions },
        };
        var preview = session.PlanAction(RefillId, NoParameters, Scope(new() { Limit = 1 }), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.False(preview.Scope.QueryScope!.Query.IncludeEmpty);
        Assert.Equal(3, preview.Scope.RowKeys!.Count);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Single(preview.Plan.SkippedRows);
        var fruit = Assert.Single(session.QueryCollection(HeldId, new() { Text = "0x400050F0" }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        var excluded = session.PlanAction(RefillId, NoParameters, Scope(new() { Limit = 1 }, fruit.Key), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, excluded.Scope.RowKeys!.Count);
        Assert.DoesNotContain(fruit.Key, excluded.Scope.RowKeys);
        Assert.Single(excluded.Plan.Changes);
        Assert.True(session.ApplyActionPlan(excluded.PlanId!, excluded.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);

        var hiddenOnly = session.PlanAction(RefillId, NoParameters, Scope(new() { Text = "Empty slot" }), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(hiddenOnly.Scope.RowKeys!);
        Assert.Empty(hiddenOnly.Plan.Changes);
        Assert.Empty(hiddenOnly.Plan.SkippedRows);
        var shown = session.PlanAction(RefillId, NoParameters, Scope(new()
        {
            IncludeEmpty = true, Filters = new Dictionary<string, string> { ["group"] = "key" }, Limit = 17,
        }), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(shown.Scope.QueryScope!.Query.IncludeEmpty);
        Assert.Equal(SemanticLayout.HeldKeyCapacity, shown.Scope.RowKeys!.Count);
        Assert.Equal(SemanticLayout.HeldKeyCapacity, shown.Plan.SkippedRows.Count);
        Assert.Empty(shown.Plan.Changes);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }
}
