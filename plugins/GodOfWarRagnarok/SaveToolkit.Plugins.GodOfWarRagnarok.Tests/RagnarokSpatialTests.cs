using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokSpatialTests
{
    [Theory]
    [InlineData("axe", 28)]
    [InlineData("blades", 26)]
    [InlineData("spear", 27)]
    public void WeaponGraphsAreAttributedUnknownAndNavigateToReadOnlyReferenceFields(string weapon, int count)
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(f => f.FileName == "GOWRSAVE0");
        var plugin = new GodOfWarRagnarokPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08330");
        using var document = plugin.Open(file, context);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        Assert.Contains(root.Tabs, tab => tab.Title == "Skills");
        var graph = Assert.Single(document.ExpandNode($"skill-tree/{weapon}").OfType<GraphNode>());
        SpatialViewValidation.Validate(graph);
        Assert.Equal(count, graph.Items.Count);
        Assert.Equal(3, graph.Items.Select(item => item.Branch).Distinct().Count());
        Assert.Contains("Unknown", graph.TextAlternative);
        Assert.NotEmpty(graph.Edges);
        var table = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode($"skill-details/{weapon}")));
        foreach (var item in graph.Items)
        {
            Assert.Equal(GraphState.Unknown, item.State);
            Assert.Contains("CC-BY-SA-3.0", item.Description);
            Assert.Contains("godofwar.fandom.com", item.Description);
            Assert.Equal(ViewTargetKind.Field, item.Target!.Kind);
            Assert.Equal(new[] { "tab:Skills", $"section:skill-tree/{weapon}", $"section:skill-details/{weapon}" }, item.Target.Path);
            var row = Assert.Single(table.Rows, row => row.Key == item.Id);
            var field = Assert.IsType<FieldNode>(row.Cells["skill"]);
            Assert.Equal(item.Target.Id, field.ValueId);
            Assert.True(Assert.IsType<ReferenceSpec>(field.Spec).ReadOnly);
            Assert.Equal(RagnarokSkillReferences.CatalogId, ((ReferenceSpec)field.Spec).CatalogId);
            Assert.Equal(Confidence.Unknown, field.Confidence);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.NotEmpty(document.ApplyEdits([new(field.ValueId, FieldValue.Of("made-up-skill"))]).Errors);
        }
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SkillPackPreservesProvenanceAndExplicitUnknownRequirementsWithEquivalentFallback()
    {
        var plugin = new GodOfWarRagnarokPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08330");
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog(RagnarokSkillReferences.CatalogId));
        Assert.Equal(81, catalog.Count);
        Assert.Equal("1.0.0", catalog.Pack.Version);
        Assert.Equal("CC-BY-SA-3.0", catalog.Dataset.Attribution.License);
        var pinned = new RagnarokSkillReferences(context);
        var fallback = new RagnarokSkillReferences(IReferenceContext.Empty);
        foreach (var (weapon, _) in RagnarokSpatialViews.Weapons)
        {
            var records = pinned.Weapon(weapon);
            Assert.Equal(records.Select(r => r.Key), fallback.Weapon(weapon).Select(r => r.Key));
            var keys = records.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var record in records)
            {
                Assert.Equal(record.AttributionText, fallback.Weapon(weapon).Single(r => r.Key == record.Key).AttributionText);
                Assert.Equal("Unknown", record.Attributes["saveHash"]);
                Assert.Equal("Unknown", record.Attributes["savedState"]);
                Assert.Null(record.Limit);
                Assert.Null(record.AssetId);
                Assert.All(record.Attributes["prerequisites"].Split(';', StringSplitOptions.RemoveEmptyEntries), key => Assert.Contains(key, keys));
            }
        }
        var axe = Assert.IsType<GraphNode>(RagnarokSpatialViews.SkillTree("axe", pinned)[0]);
        Assert.Contains(new GraphEdge("axe/frost-awaken-ii", "axe/frost-awaken-iv"), axe.Edges);
        Assert.Contains(new GraphEdge("axe/frost-awaken-iii", "axe/frost-awaken-iv"), axe.Edges);
        Assert.Equal("Unknown", catalog.Find("blades/immolation")!.Attributes["xp"]);
        Assert.Equal("0", catalog.Find("axe/frozen-breach")!.Attributes["xp"]);
    }

    [Fact]
    public void RealmMapIsLinkFirstHasNoCoordinatesAndAllTargetsResolveWithoutChangingBytes()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(f => f.FileName == "GOWRSAVE0");
        using var document = new GodOfWarRagnarokPlugin().Open(file);
        var map = Assert.Single(RagnarokFormatTests.Flatten(document.BuildView()).OfType<MapNode>());
        SpatialViewValidation.Validate(map);
        Assert.Equal(9, map.Markers.Count);
        Assert.Null(map.Background);
        Assert.Null(map.CoordinateAttribution);
        Assert.False(map.CoordinateEmbeddingPermitted);
        Assert.Equal("mapgenie.io", new Uri(map.ExternalSource!.Url).Host);
        Assert.Contains("external link only", map.ExternalSource.Attribution);
        var details = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("realm-details")));
        foreach (var marker in map.Markers)
        {
            Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
            Assert.Equal(Confidence.Unknown, marker.Confidence);
            Assert.Null(marker.X);
            Assert.Null(marker.Y);
            Assert.Equal("godofwar.fandom.com", new Uri(marker.ExternalLink!.Url).Host);
            var field = Assert.IsType<FieldNode>(Assert.Single(details.Rows, row => row.Key == marker.Id).Cells["realm"]);
            Assert.Equal(field.ValueId, marker.Target!.Id);
            Assert.Collection(marker.Target.Path, part => Assert.Equal("tab:Realm map", part), part => Assert.Equal("section:realm-details", part));
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.True(field.Spec.ReadOnly);
        }
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CataloguedResourceIdsUseReadOnlyPickersAndCannotReplaceTheQuantityBinding()
    {
        var file = SampleSaves.Load("PPSA08330").AllDiskFiles.First(f => f.FileName == "GOWRSAVE0");
        var plugin = new GodOfWarRagnarokPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08330");
        using var document = plugin.Open(file, context);
        _ = document.BuildView();
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("resources", new() { Text = "Hacksilver" });
        foreach (var row in page.Rows)
        {
            var identity = Assert.IsType<FieldNode>(row.Cells["id"]);
            var spec = Assert.IsType<ReferenceSpec>(identity.Spec);
            Assert.True(spec.ReadOnly);
            Assert.NotNull(context.Catalog(spec.CatalogId)!.Find(identity.Value.AsString()));
            Assert.NotEmpty(document.ApplyEdits([new(identity.ValueId, FieldValue.Of("7E3FBE3EB167B14F"))]).Errors);
            Assert.False(Assert.IsType<FieldNode>(row.Cells["quantity"]).Spec.ReadOnly);
        }
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
