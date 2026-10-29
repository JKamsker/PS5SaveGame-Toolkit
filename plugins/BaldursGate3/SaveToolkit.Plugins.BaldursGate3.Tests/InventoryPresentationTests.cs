using System.Buffers.Binary;
using System.IO.Compression;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class InventoryPresentationTests
{
    private const int ItemCount = QueryableCollectionLimits.MaxPageSize + 5;

    [Fact]
    public void SavedItemsUseOneTableWithCompleteFilteringSortingAndOccurrenceProvenance()
    {
        var items = Items();
        var table = Assert.Single(items.Build().OfType<QueryableTableNode>());
        Assert.Empty(items.Build().OfType<SectionNode>());
        Assert.Null(table.EmptyRows);
        Assert.Equal(ItemCount, table.TotalCount);
        Assert.Contains(table.Columns, column => column.Key == "provenance");
        var first = items.Query(new() { Limit = QueryableCollectionLimits.MaxPageSize, SortColumn = "stats" });
        var last = items.Query(new() { Offset = QueryableCollectionLimits.MaxPageSize, Limit = 5, SortColumn = "stats" });
        var rows = first.Rows.Concat(last.Rows).ToArray();
        Assert.Equal(ItemCount, rows.Length);
        Assert.Equal(ItemCount, rows.Select(row => row.Key).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, ItemCount).Select(Stats), rows.Select(row => Assert.IsType<FieldNode>(row.Cells["stats"]).Value.AsString()));
        Assert.All(rows, row => Assert.False(row.IsEmpty));

        var target = rows[^1];
        var provenance = Assert.IsType<TextNode>(target.Cells["provenance"]).Text;
        var filtered = items.Query(new()
        {
            Text = provenance, SortColumn = "stats", Descending = true, Limit = 1,
            Filters = new Dictionary<string, string> { ["stats"] = Stats(ItemCount - 1).ToLowerInvariant() },
        });
        Assert.Equal(ItemCount, filtered.TotalCount);
        Assert.Equal(1, filtered.FilteredCount);
        Assert.Equal(0, filtered.HiddenEmptyCount);
        Assert.Equal(target.Key, Assert.Single(filtered.Rows).Key);
        Assert.Equal(rows.Reverse().Select(row => row.Key), items.Query(new() { SortColumn = "stats", Descending = true,
            Limit = QueryableCollectionLimits.MaxPageSize }).Rows.Select(row => row.Key).Concat(items.Query(new()
            { SortColumn = "stats", Descending = true, Offset = QueryableCollectionLimits.MaxPageSize, Limit = 5 }).Rows.Select(row => row.Key)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownTemplatesAndZeroQuantityAreRetainedRegardlessOfEmptyVisibility(bool? includeEmpty)
    {
        var page = Items().Query(new() { IncludeEmpty = includeEmpty, Limit = 1 });
        Assert.Equal(ItemCount, page.TotalCount);
        Assert.Equal(ItemCount, page.FilteredCount);
        Assert.Equal(0, page.HiddenEmptyCount);
        var row = Assert.Single(page.Rows);
        Assert.False(row.IsEmpty);
        Assert.Equal("(missing template)", Assert.IsType<FieldNode>(row.Cells["name"]).Value.AsString());
        Assert.Contains("candidate quantity: 0", Assert.IsType<TextNode>(row.Cells["ownership"]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SavedItemQueriesRejectInvalidRangesAndColumns()
    {
        var items = Items();
        foreach (var query in new QueryableCollectionQuery[]
        {
            new() { Offset = -1 }, new() { Limit = 0 }, new() { Limit = QueryableCollectionLimits.MaxPageSize + 1 },
            new() { Cursor = "unsupported" }, new() { SortColumn = "unknown" },
            new() { Filters = new Dictionary<string, string> { ["unknown"] = "value" } },
        })
            Assert.Throws<ArgumentException>(() => items.Query(query));
        Assert.Empty(items.Query(new() { Offset = ItemCount }).Rows);
    }

    [Fact]
    public void SparseContainerKeysRemainOccupiedAndQueryScopedSwapUsesNativeIdentity()
    {
        var source = LiveNewAgeFixture.Source();
        var bytes = source.Write();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(source.Slots[0].KeyOffset), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(source.Slots[1].KeyOffset), ushort.MaxValue);
        var original = LsmfFixture.Resource(bytes);
        var plugin = new BaldursGate3Plugin();
        var root = Path.Combine(Path.GetTempPath(), "SaveToolkit.Bg3.Tests", Guid.NewGuid().ToString("N"));
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = root });
        using var zipBytes = new MemoryStream();
        using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("PPSA18463/sdimg_SAVE/Globals.lsf").Open();
            entry.Write(original);
        }
        var workspace = store.Create("sparse-container", [new ImportSource("fixture.zip", () => new MemoryStream(zipBytes.ToArray()))]);
        var file = workspace.Set.AllDiskFiles.Single();
        using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(file));
        using var session = new DocumentSession("sparse-container", file, plugin, document);
        _ = session.BuildView();
        var table = Assert.Single(session.Expand(source.Prefix).OfType<QueryableTableNode>(), node => node.CollectionId == source.Prefix + "/slots");
        Assert.Null(table.EmptyRows);
        Assert.Equal(2, table.TotalCount);
        var page = session.QueryCollection(table.CollectionId, new() { IncludeEmpty = false });
        Assert.Equal(2, page.FilteredCount);
        Assert.Equal(0, page.HiddenEmptyCount);
        Assert.All(page.Rows, row => Assert.False(row.IsEmpty));
        Assert.Equal(source.Slots.Select(slot => source.Prefix + "/" + slot.Key), page.Rows.Select(row => row.Key));
        Assert.Equal([0, ushort.MaxValue], page.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["slot"]).Value.AsInt32()));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());

        var scope = new ActionQueryScope(table.CollectionId, new() { IncludeEmpty = false, Limit = 1 }, session.State.Revision);
        var preview = session.PlanAction(LsmfLiveActions.Swap, new Dictionary<string, System.Text.Json.JsonElement>(), new ActionScope { QueryScope = scope });
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(page.Rows.Select(row => row.Key), preview.Scope.RowKeys);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = Assert.IsType<BaldursGate3Document>(plugin.Open(MemorySaveSet.SingleFile("Globals.lsf", edited)));
        var changed = reopened.Query(table.CollectionId, new());
        Assert.Equal(page.Rows.Select(row => row.Key), changed.Rows.Select(row => row.Key));
        Assert.Equal([ushort.MaxValue, 0], changed.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["slot"]).Value.AsInt32()));
        var allowed = source.Slots.SelectMany(slot => Enumerable.Range(slot.KeyOffset, sizeof(ushort))).ToHashSet();
        var after = LiveNewAgeFixture.Source(bytes);
        after.Swap(after.Slots[0], after.Slots[1]);
        var expected = after.Write();
        Assert.Equal(LsmfFixture.Resource(expected), edited);
        Assert.All(Enumerable.Range(0, bytes.Length).Where(index => bytes[index] != expected[index]), index => Assert.Contains(index, allowed));
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static string Stats(int index) => $"OBJ_{index:D3}";

    private static SavedItemInspection Items()
    {
        var nodes = new List<ResourceNode> { new("Items", -1) };
        var attributes = new List<ResourceAttribute> { new("Quantity", LarianAttributeType.Int, BitConverter.GetBytes(0)) };
        for (var index = ItemCount - 1; index >= 0; index--)
        {
            var node = new ResourceNode("Item", 0);
            node.Attributes.AddRange([0, attributes.Count]);
            attributes.Add(new("Stats", LarianAttributeType.FixedString, System.Text.Encoding.UTF8.GetBytes(Stats(index) + '\0')));
            nodes.Add(node);
        }
        var resource = new LarianResource("synthetic", nodes, attributes, [0]);
        return new([new("Globals.lsf", [], resource)], new Bg3References(IReferenceContext.Empty),
            (id, label, value) => new FieldNode(id, label, new TextSpec { ReadOnly = true }, value));
    }
}
