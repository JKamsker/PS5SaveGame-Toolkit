using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.SuckerPunch.Tests.YoteiTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiViewTests
{
    private const string GearId = "yotei/gear-registry";

    [Fact]
    public void GearPagingIsBoundedCompleteSearchableAndDeterministicallySorted()
    {
        using var doc = Open(Slot("suckerpunchUser4"));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(doc);
        var metadata = Assert.Single(queryable.Collections);
        Assert.Equal(GearId, metadata.Id);
        Assert.Equal(1037, metadata.TotalCount);
        var all = new List<TableRow>();
        for (var offset = 0; offset < metadata.TotalCount; offset += 73)
        {
            var page = queryable.Query(GearId, new QueryableCollectionQuery { Offset = offset, Limit = 73, SortColumn = "id" });
            Assert.InRange(page.Rows.Count, 1, 73);
            Assert.Equal(metadata.TotalCount, page.FilteredCount);
            all.AddRange(page.Rows);
        }
        Assert.Equal(metadata.TotalCount, all.Select(row => row.Key).Distinct().Count());
        Assert.Equal(all.Select(row => row.Key).Order(StringComparer.OrdinalIgnoreCase), all.Select(row => row.Key));
        var found = queryable.Query(GearId, new QueryableCollectionQuery { Text = "HalfShog101" });
        Assert.Equal("HalfShog101", Assert.Single(found.Rows).Key);
        Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(found.Rows[0].Cells["a"]).Confidence);
        Assert.True(Assert.IsType<FieldNode>(found.Rows[0].Cells["a"]).Spec.ReadOnly);
        Assert.False(doc.ApplyEdits([new("yotei/gear/HalfShog101/byte0", FieldValue.Of(0L))]).Succeeded);
        var filtered = queryable.Query(GearId, new QueryableCollectionQuery
        { Filters = new Dictionary<string, string> { ["category"] = "Mask", ["state"] = "Unknown" }, SortColumn = "id", Descending = true });
        Assert.All(filtered.Rows, row => Assert.Equal("Mask", Assert.IsType<TextNode>(row.Cells["category"]).Text));
        Assert.Equal(filtered.Rows.Select(row => row.Key).OrderDescending(StringComparer.OrdinalIgnoreCase), filtered.Rows.Select(row => row.Key));
        Assert.Empty(queryable.Query(GearId, new QueryableCollectionQuery { Offset = int.MaxValue }).Rows);
        Assert.Throws<ArgumentException>(() => queryable.Query(GearId, new QueryableCollectionQuery { Limit = 201 }));
        Assert.Throws<ArgumentException>(() => queryable.Query(GearId, new QueryableCollectionQuery { Offset = -1 }));
        Assert.Throws<ArgumentException>(() => queryable.Query(GearId, new QueryableCollectionQuery { Cursor = "bad" }));
        Assert.Throws<ArgumentException>(() => queryable.Query(GearId, new QueryableCollectionQuery { SortColumn = "unknown" }));
        Assert.Throws<ArgumentException>(() => queryable.Query(GearId, new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["unknown"] = "x" } }));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public void OpaqueByteSortUsesNumericOrderAndAscendingIdTies(string column)
    {
        var bytes = Slot("suckerpunchUser4").ReadAllBytes();
        var start = bytes.AsSpan().IndexOf("\x0d\x04\0\0\x0b\0\0\0HalfShog101"u8);
        var records = ReadTable(bytes, start, 1037, 2);
        var index = column == "a" ? 0 : 1;
        bytes[records[0].Data + index] = 2;
        bytes[records[1].Data + index] = 15;
        Repair(bytes);
        using var doc = Open(WithBytes(bytes));
        var page = ((IQueryableDocument)doc).Query(GearId, new QueryableCollectionQuery { SortColumn = column, Descending = true });
        Assert.Equal(records[1].Name, page.Rows[0].Key);
        Assert.Equal(records[0].Name, page.Rows[1].Key);
    }

    [Fact]
    public void EveryGenerationHasReadOnlyStatsAndIdenticalRegistryBytesWithoutOwnershipClaims()
    {
        byte[]? previous = null;
        foreach (var file in Files)
        {
            var bytes = file.ReadAllBytes();
            if (Word(bytes, 0) == 0) continue;
            using var doc = Open(file);
            var stats = ((TabsNode)doc.BuildView()).Tabs.Single(tab => tab.Title == "Statistics").Children.OfType<TableNode>().Single();
            Assert.Equal(33, stats.Rows.Count);
            Assert.All(stats.Rows, row =>
            {
                var field = Assert.IsType<FieldNode>(row.Cells["value"]);
                Assert.True(field.Spec.ReadOnly);
                Assert.Equal(Confidence.Inferred, field.Confidence);
                Assert.False(doc.ApplyEdits([new(field.ValueId, FieldValue.Of(1L))]).Succeeded);
            });
            var start = bytes.AsSpan().IndexOf("\x0d\x04\0\0\x0b\0\0\0HalfShog101"u8);
            var gear = ReadTable(bytes, start, 1037, 2);
            var flags = gear.SelectMany(row => bytes.AsSpan(row.Data, 2).ToArray()).ToArray();
            if (previous is not null) Assert.Equal(previous, flags);
            previous = flags;
            Assert.Equal(bytes, Assert.Single(doc.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void AttributedGuidesPublishUnknownSpatialStatesBrowseOnlyPickersAndTextFallbacks()
    {
        var plugin = new SuckerPunchPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, Title);
        using var doc = plugin.Open(Slot("suckerpunchUser4"), context);
        var view = (TabsNode)doc.BuildView();
        var graph = view.Tabs.Single(tab => tab.Title == "Techniques").Children.OfType<GraphNode>().Single();
        SpatialViewValidation.Validate(graph);
        Assert.Equal(10, graph.Items.Count);
        Assert.Empty(graph.Edges);
        Assert.All(graph.Items, item => { Assert.Equal(GraphState.Unknown, item.State); Assert.NotNull(item.Target); });
        var map = view.Tabs.Single(tab => tab.Title == "World").Children.OfType<MapNode>().Single();
        SpatialViewValidation.Validate(map);
        Assert.Equal(16, map.Markers.Count);
        Assert.NotNull(map.Background);
        var placed = Assert.Single(map.Markers, marker => marker.X.HasValue);
        Assert.Equal("horizon-view", placed.Id);
        Assert.Equal(960, placed.X);
        Assert.Equal(550, placed.Y);
        Assert.All(map.Markers, marker => { Assert.Equal(MapMarkerStatus.Unknown, marker.Status); Assert.NotNull(marker.Target); });
        foreach (var title in new[] { "Techniques", "World" })
        {
            var table = view.Tabs.Single(tab => tab.Title == title).Children.OfType<TableNode>().Single();
            Assert.All(table.Rows, row =>
            {
                var field = Assert.IsType<FieldNode>(row.Cells["name"]);
                Assert.IsType<ReferenceSpec>(field.Spec);
                Assert.True(field.Spec.ReadOnly);
                Assert.Contains("owner decision", field.Description);
                Assert.Contains("owner decision", Assert.IsType<TextNode>(row.Cells["source"]).Text);
            });
        }
        using var fallback = plugin.Open(Slot("suckerpunchUser4"), new WithoutAssets(context));
        var fallbackMap = ((TabsNode)fallback.BuildView()).Tabs.Single(tab => tab.Title == "World").Children.OfType<MapNode>().Single();
        SpatialViewValidation.Validate(fallbackMap);
        Assert.Null(fallbackMap.Background);
        Assert.All(fallbackMap.Markers, marker => { Assert.Null(marker.X); Assert.Null(marker.Y); });
        Assert.Equal(16, fallbackMap.Markers.Count);
    }

    private sealed class WithoutAssets(IReferenceContext inner) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId, gameBuild);
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }
}
