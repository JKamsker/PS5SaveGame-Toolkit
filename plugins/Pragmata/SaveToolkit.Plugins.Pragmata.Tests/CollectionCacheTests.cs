using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class CollectionCacheTests
{
    [Theory]
    [InlineData("name", false)]
    [InlineData("name", true)]
    [InlineData("id", false)]
    [InlineData("id", true)]
    [InlineData("category", false)]
    [InlineData("category", true)]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    [InlineData("equipped", false)]
    [InlineData("equipped", true)]
    [InlineData("details", false)]
    [InlineData("details", true)]
    [InlineData("action", false)]
    [InlineData("action", true)]
    public void EveryInventorySortKeepsCompleteMembershipAndPhysicalTieBreaks(string column, bool descending)
    {
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(251));
        var authored = document.Layout.Items.Select((item, index) => new
        {
            Key = $"inventory/record-at-{item.Offset:x}", Quantity = index % 11,
            Text = column switch
            {
                "name" => $"Unmapped item 0x{index + 1:x8}",
                "id" => $"0x{index + 1:x8} / {index + 1}",
                "category" => "unmapped", "equipped" => index == 0 ? "Equipped" : "", _ => "",
            },
        }).ToArray();
        var ordered = column == "quantity"
            ? descending ? authored.OrderByDescending(row => row.Quantity) : authored.OrderBy(row => row.Quantity)
            : descending ? authored.OrderByDescending(row => row.Text, StringComparer.Ordinal) : authored.OrderBy(row => row.Text, StringComparer.Ordinal);
        var expected = ordered.ThenBy(row => row.Key, StringComparer.Ordinal).Select(row => row.Key).ToArray();
        var actual = new List<string>();
        for (var offset = 0; offset < 251; offset += 73)
        {
            var page = document.Query(InventoryCollections.CollectionId, new() { SortColumn = column, Descending = descending, Offset = offset, Limit = 73 });
            Assert.Equal(251, page.FilteredCount);
            actual.AddRange(page.Rows.Select(row => row.Key));
        }
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerFilterMutationAndLiveEditsCannotReuseStaleMatches(bool evidence)
    {
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(251));
        _ = document.BuildView();
        var array = document.Layout.Inventory!.Find(KnownHashes.Items)!;
        var id = evidence ? document.Evidence.Table(array, item => document.Field(item.Find(KnownHashes.Quantity)!)).CollectionId
            : InventoryCollections.CollectionId;
        var filters = new Dictionary<string, string> { [evidence ? "type" : "quantity"] = evidence ? "0x11111111" : "3" };
        var query = new QueryableCollectionQuery { Limit = 200, Filters = filters };
        var original = document.Query(id, query);
        Assert.Equal(evidence ? 251 : 23, original.FilteredCount);
        var target = original.Rows[0];
        if (evidence)
        {
            filters.Clear();
            filters["values"] = ((TextNode)target.Cells["values"]).Text;
        }
        else filters["quantity"] = "4";
        var changedCriteria = document.Query(id, query);
        Assert.Equal(evidence ? 1 : 23, changedCriteria.FilteredCount);
        if (!evidence) Assert.DoesNotContain(changedCriteria.Rows, row => row.Key == target.Key);
        filters.Clear();
        filters[evidence ? "values" : "quantity"] = evidence ? ((TextNode)target.Cells["values"]).Text : "3";
        var beforeEdit = document.Query(id, query);
        var quantity = Assert.IsType<FieldNode>(evidence ? target.Cells["details"] : target.Cells["quantity"]);
        Assert.True(document.ApplyEdits([new(quantity.ValueId, FieldValue.Of(99L))]).Succeeded);
        var afterEdit = document.Query(id, query);
        Assert.Equal(beforeEdit.FilteredCount - 1, afterEdit.FilteredCount);
        Assert.DoesNotContain(afterEdit.Rows, row => row.Key == target.Key);
        var live = document.Query(id, new() { Text = "99", Limit = 200 });
        Assert.Contains(live.Rows, row => row.Key == target.Key);
        Assert.True(document.ApplyEdits([new(quantity.ValueId, quantity.Value)]).Succeeded);
        Assert.Equal(beforeEdit.Rows.Select(row => row.Key), document.Query(id, query).Rows.Select(row => row.Key));
        filters.Clear();
        filters[evidence ? "type" : "quantity"] = "no-match";
        Assert.Equal(0, document.Query(id, query).FilteredCount);
        Assert.Equal(251, document.Query(id, query with { Filters = new Dictionary<string, string>() }).FilteredCount);
    }

    [Fact]
    public void RawItemAndEquippedEditsRefreshAllInventoryDescriptions()
    {
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(251));
        var equippedQuery = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["equipped"] = "Equipped" } };
        var row = Assert.Single(document.Query(InventoryCollections.CollectionId, equippedQuery).Rows);
        var first = document.Layout.Items[0];
        var item = document.Field(first.Find(KnownHashes.ItemId)!, raw: true);
        var equipped = document.Field(document.Layout.Inventory!.Find(KnownHashes.EquippedItem)!, raw: true);
        Assert.True(document.ApplyEdits([new(item.ValueId, FieldValue.Of(0UL))]).Succeeded);
        Assert.Empty(document.Query(InventoryCollections.CollectionId, equippedQuery).Rows);
        Assert.True(document.ApplyEdits([new(equipped.ValueId, FieldValue.Of(0UL))]).Succeeded);
        var edited = Assert.Single(document.Query(InventoryCollections.CollectionId, equippedQuery).Rows);
        Assert.Equal(row.Key, edited.Key);
        Assert.Equal("0x00000000 / 0", ((TextNode)edited.Cells["id"]).Text);
        Assert.Equal("0", ((FieldNode)edited.Cells["name"]).Value.AsString());
        Assert.Equal(row.Key, Assert.Single(document.Query(InventoryCollections.CollectionId, new() { Text = "0x00000000" }).Rows).Key);
        Assert.Equal(251, document.Layout.Items.Count);
    }
}
