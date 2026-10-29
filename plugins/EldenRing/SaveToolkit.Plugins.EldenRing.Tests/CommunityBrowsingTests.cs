using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class CommunityBrowsingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DlcInventoryNameUsesItsOwnBrowseOnlyPickerAndNeverEnablesNewWrites(bool readOnly)
    {
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 0, 0xB01EAB90, 3, 1); // Scadutree Fragment, absent from old name pack.
        if (readOnly) fixture.Save.Write32(SyntheticSave.UserData, 91);
        var file = fixture.Save.File();
        var plugin = new EldenRingPlugin();
        var context = CommunityReferenceTests.Context();
        Assert.Null(context.Catalog(ReferenceBrowser.CatalogId)!.Find("0x401EAB90"));
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("eldenring-community-items", WriteTestSupport.Sample(), plugin, document, references: context);
        _ = session.BuildView();
        var row = Assert.Single(session.QueryCollection("slots/0/inventory/held", new() { Text = "Scadutree Fragment" }).Rows);
        var field = Assert.IsType<FieldNode>(row.Cells["item"]);
        Assert.Equal("Scadutree Fragment", field.Label);
        Assert.Equal("0x401EAB90", field.Value.AsString());
        Assert.Equal(CommunityReferences.ItemsCatalogId, Assert.IsType<ReferenceSpec>(field.Spec).CatalogId);
        Assert.True(field.Spec.ReadOnly);
        Assert.Contains("owner decision 2026-10-28", field.Description);
        Assert.Equal("Scadutree Fragment", session.FindReference(field.ValueId, field.Value.AsString()).Label);
        Assert.Contains(session.SearchReferences(field.ValueId, new() { Text = "Scadutree Fragment", Category = "goods" }).Items,
            candidate => candidate.RawId == "0x401EAB90");
        Assert.False(session.ApplyEdits([WriteTestSupport.Incoming(field.ValueId, "0x400050F0")]).Ok);
        Assert.False(session.ApplyEdits([WriteTestSupport.Incoming(row.Key + "/quantity", 9)]).Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Fact]
    public void SampleNamesAndNewCategoriesKeepBothSourcesAndExactBytes()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        using var document = new EldenRingPlugin().Open(file, CommunityReferenceTests.Context());
        var row = Assert.Single(((IQueryableDocument)document).Query("slots/0/inventory/held", new() { Text = "Rowa Fruit" }).Rows);
        var field = Assert.IsType<FieldNode>(row.Cells["item"]);
        Assert.Equal("Rowa Fruit", field.Label);
        Assert.Equal(ReferenceBrowser.CatalogId, Assert.IsType<ReferenceSpec>(field.Spec).CatalogId);
        Assert.Contains("Crafting: Plant", Assert.IsType<TextNode>(row.Cells["sourceGroup"]).Text);
        Assert.Contains("MIT", field.Description);
        Assert.Contains("The Grand Archives", field.Description);
        Assert.Contains("No licence published", field.Description);
        WriteTestSupport.EqualBytes(original, document);
    }

    [Fact]
    public void EveryCommunityMapTargetPublishesThroughCoreWithFullParentPathAndUnknownStatus()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var plugin = new EldenRingPlugin();
        var context = CommunityReferenceTests.Context();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("eldenring-community-progress", file, plugin, document, references: context);
        _ = session.BuildView();
        var root = session.Expand(CommunityReferences.ProgressSectionId);
        Assert.Equal(1748, Assert.Single(root.OfType<QueryableTableNode>()).TotalCount);
        var slot = Assert.Single(root.OfType<SectionNode>());
        var regions = session.Expand(slot.LazyId!).OfType<SectionNode>().ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            var nodes = session.Expand(region.LazyId!);
            var map = Assert.Single(nodes.OfType<MapNode>());
            var table = Assert.Single(nodes.OfType<TableNode>());
            SpatialViewValidation.Validate(map);
            VisualAssetTests.ValidateMap(map, context);
            Assert.InRange(map.Markers.Count, 1, 100);
            Assert.Equal(table.Rows.Count, map.Markers.Count);
            foreach (var marker in map.Markers)
            {
                Assert.True(keys.Add(marker.Id));
                Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
                Assert.Equal(Confidence.Inferred, marker.Confidence);
                Assert.Contains("No licence published", marker.Description);
                Assert.Contains("not console-validated", marker.Description);
                var target = Assert.IsType<ViewTarget>(marker.Target);
                Assert.Equal(["tab:World / progress", "section:community-progress", "section:community-progress/0",
                    "section:" + region.LazyId, "row:" + marker.Id], target.Path);
                var field = Assert.IsType<FieldNode>(table.Rows.Single(row => row.Key == marker.Id).Cells["raw"]);
                Assert.Equal(target.Id, field.ValueId);
                Assert.Equal(field.Value, document.GetValue(target.Id));
                Assert.True(field.Spec.ReadOnly);
            }
        }
        Assert.Equal(1748, keys.Count);
        foreach (var (eventId, set) in new[] { (76100, true), (76101, true), (76102, false) })
        {
            var row = Assert.Single(session.QueryCollection("slots/0/community-progress",
                new() { Filters = new Dictionary<string, string> { ["event"] = eventId.ToString(System.Globalization.CultureInfo.InvariantCulture) } }).Rows);
            var raw = Assert.IsType<FieldNode>(row.Cells["raw"]);
            Assert.Equal(set, raw.Value.AsBool());
            Assert.False(session.ApplyEdits([WriteTestSupport.Incoming(raw.ValueId, !set)]).Ok);
        }
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        WriteTestSupport.EqualBytes(original, document);
    }

    [Fact]
    public void NewCatalogsPageFilterAndPreserveRuneUndoAlongsideRegulationReferenceVersions()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var plugin = new EldenRingPlugin();
        var context = CommunityReferenceTests.Context();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("eldenring-community-catalogs", file, plugin, document, references: context);
        _ = session.BuildView(); _ = session.Expand("characters/0");
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming("slots/0/runes", 1146)]).Ok);
        var edited = WriteTestSupport.Bytes(document);
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("catalog/param-rows"));
        _ = session.Expand("catalog/community-items");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < 3221; offset += 200)
        {
            var page = session.QueryCollection("catalog/community-items", new() { Offset = offset, Limit = 200, SortColumn = "id" });
            Assert.Equal(3221, page.TotalCount);
            Assert.InRange(page.Rows.Count, 1, 200);
            Assert.All(page.Rows, row => Assert.True(keys.Add(row.Key)));
        }
        Assert.Equal(3221, keys.Count);
        var flags = session.QueryCollection("catalog/community-flags", new()
            { Filters = new Dictionary<string, string> { ["category"] = "event-flag" }, Limit = 1 });
        Assert.Equal(1748, flags.FilteredCount);
        var rows = session.QueryCollection("catalog/community-items", new()
            { Filters = new Dictionary<string, string> { ["category"] = "goods" }, Text = "Scadutree Fragment" });
        Assert.Equal("Scadutree Fragment", Assert.IsType<TextNode>(Assert.Single(rows.Rows).Cells["name"]).Text);
        _ = session.Expand("regulation/metadata");
        var table = Assert.Single(session.QueryCollection(RegulationViews.CollectionId, new() { Text = "EquipParamGoods.param" }).Rows);
        var schema = Assert.IsType<TextNode>(table.Cells["schema"]).Text;
        Assert.Contains("117 fields; source data version 3, save 3", schema);
        Assert.Contains("does not prove PS5 compatibility", schema);
        Assert.Contains("No licence published", schema);
        WriteTestSupport.EqualBytes(edited, document);
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(original, document);
    }

    [Fact]
    public void MissingAndUntrustedCatalogsRetainRawFallbacksAndBoundedQueries()
    {
        var browser = new CommunityCatalogView("test", "No pack", null);
        Assert.Empty(browser.Query(new()).Rows);
        Assert.Contains("No compatible", browser.Collection.Description);
        var source = CommunityReferenceTests.Context().Catalog(CommunityReferences.ItemsCatalogId)!;
        var oversized = new ProgressTestCatalog(source, [], 20_001);
        Assert.Throws<InvalidOperationException>(() => new CommunityCatalogView("test", "Oversized", oversized).Query(new()));
        Assert.Equal(0, oversized.Searches);
        var shortPage = new ProgressTestCatalog(source, [], 1);
        Assert.Throws<InvalidOperationException>(() => new CommunityCatalogView("test", "Bad page", shortPage).Query(new()));
    }
}
