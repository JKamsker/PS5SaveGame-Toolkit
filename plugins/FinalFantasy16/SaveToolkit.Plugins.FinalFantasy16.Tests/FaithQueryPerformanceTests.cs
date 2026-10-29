using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithQueryPerformanceTests
{
    [Theory]
    [InlineData("slot", false)]
    [InlineData("slot", true)]
    [InlineData("id", false)]
    [InlineData("id", true)]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    [InlineData("name", false)]
    [InlineData("name", true)]
    [InlineData("category", false)]
    [InlineData("category", true)]
    [InlineData("flags", false)]
    [InlineData("flags", true)]
    [InlineData("details", false)]
    [InlineData("details", true)]
    [InlineData("icon", false)]
    [InlineData("icon", true)]
    [InlineData("action", false)]
    [InlineData("action", true)]
    public void AllColumnSortsKeepNumericSlotTiesAcrossPages(string column, bool descending)
    {
        var bytes = FaithPerformanceData.CapacitySave();
        const int PayloadOffset = FaithContainer.HeaderSize + FaithBlock.HeaderSize;
        const int RecordSize = 16;
        const int QuantityGroups = 3;
        for (var slot = 0; slot < FaithInventory.Capacity; slot++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(PayloadOffset + slot * RecordSize + sizeof(int)), slot % QuantityGroups);
        FaithTestData.FixCrc(bytes);
        var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
        var catalog = new FaithCatalog(IReferenceContext.Empty);
        var engine = new FaithInventoryQuery(bytes, inventory, catalog);
        var metadata = new FaithInventoryView(bytes, inventory, catalog).Collection;
        var slots = Enumerable.Range(0, FaithInventory.Capacity).ToArray();
        // Unknown names/IDs/flags increase with the slot. Category/evidence/decorations are equal.
        // Quantity groups explicitly retain ascending native slots even for descending quantities.
        var groups = Enumerable.Range(0, QuantityGroups);
        if (descending) groups = groups.Reverse();
        var expected = column == "quantity"
            ? groups.SelectMany(group => slots.Where(slot => slot % QuantityGroups == group))
            : descending && column is "slot" or "id" or "name" or "flags" ? slots.Reverse() : slots;
        var actual = Enumerable.Range(0, 6).SelectMany(page => engine.Query(new()
        {
            SortColumn = column, Descending = descending, Offset = page * 200, Limit = 200,
        }, item => new(item.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>()), metadata).Rows);
        Assert.Equal(expected.Select(slot => slot.ToString(System.Globalization.CultureInfo.InvariantCulture)), actual.Select(row => row.Key));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("slot", false)]
    [InlineData("slot", true)]
    public void UnfilteredSlotPagesAvoidFullInventoryMaterialization(string? sort, bool descending)
    {
        var bytes = FaithPerformanceData.CapacitySave();
        var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
        var references = new FaithCountingReferences(IReferenceContext.Empty);
        var catalog = new FaithCatalog(references);
        var engine = new FaithInventoryQuery(bytes, inventory, catalog);
        var metadata = new FaithInventoryView(bytes, inventory, catalog).Collection;
        var query = new QueryableCollectionQuery { Offset = 1100, Limit = 1, SortColumn = sort, Descending = descending };
        var rendered = new List<int>();
        TableRow Row(FaithItem item)
        {
            rendered.Add(item.Slot);
            return new($"inventory/{item.Slot}", new Dictionary<string, ViewNode>());
        }
        _ = engine.Query(query, Row, metadata);
        rendered.Clear();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = engine.Query(query, Row, metadata);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 16 * 1024, $"A one-row slot page allocated {allocated:N0} bytes.");
        Assert.Empty(references.CatalogCalls);
        Assert.Equal([descending ? 99 : 1100], rendered);
        Assert.Equal(FaithInventory.Capacity, page.TotalCount);
        Assert.Equal(FaithInventory.Capacity, page.FilteredCount);
        Assert.Single(page.Rows);
    }

    [Fact]
    public void NumericFiltersReadOnlyNativeFieldsAndRenderOnlyMatches()
    {
        var bytes = FaithPerformanceData.CapacitySave();
        var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
        var references = new FaithCountingReferences(IReferenceContext.Empty);
        var catalog = new FaithCatalog(references);
        var engine = new FaithInventoryQuery(bytes, inventory, catalog);
        var metadata = new FaithInventoryView(bytes, inventory, catalog).Collection;
        var rendered = new List<int>();
        var page = engine.Query(new()
        {
            Filters = new Dictionary<string, string> { ["quantity"] = "1102" }, SortColumn = "id", Descending = true,
        }, item => { rendered.Add(item.Slot); return new($"inventory/{item.Slot}", new Dictionary<string, ViewNode>()); }, metadata);
        Assert.Equal([1100], rendered);
        Assert.Equal(1, page.FilteredCount);
        Assert.Empty(references.CatalogCalls);
    }

    [Fact]
    public void SearchAndFiltersRetainEvidenceFlagsAndDecorativeColumnSemantics()
    {
        var bytes = FaithPerformanceData.CapacitySave(mapped: true);
        using var document = new FinalFantasy16Plugin().Open(FaithTestData.File(bytes));
        var queryable = (IQueryableDocument)document;
        var query = new QueryableCollectionQuery
        {
            Text = "earlier CURATED reference", SortColumn = "name", Descending = true,
            Filters = new Dictionary<string, string> { ["flags"] = "0x0000044c", ["action"] = "", ["icon"] = "" },
        };
        var page = queryable.Query(FaithInventoryView.CollectionId, query);
        Assert.Equal(1, page.FilteredCount);
        Assert.Equal("inventory/1100", Assert.Single(page.Rows).Key);
        var empty = queryable.Query(FaithInventoryView.CollectionId, query with
            { Filters = new Dictionary<string, string> { ["icon"] = "unavailable" } });
        Assert.Equal(0, empty.FilteredCount);
        Assert.Empty(empty.Rows);
    }
}
