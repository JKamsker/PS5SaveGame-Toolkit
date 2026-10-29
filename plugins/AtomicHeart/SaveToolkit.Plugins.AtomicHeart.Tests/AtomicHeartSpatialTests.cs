using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartSpatialTests
{
    [Fact]
    public void SampleGraphsHaveUnknownStatesNoInventedEdgesAndResolvableEvidenceTargets()
    {
        using var document = WriteTestData.Plugin.Open(WriteTestData.Sample(), WriteTestData.References());
        var skills = Assert.IsType<GraphNode>(Assert.Single(document.ExpandNode("spatial/skills/0")));
        Assert.Equal(3, skills.Items.Count);
        Assert.All(skills.Items, i => Assert.Contains("two serialized zero counts", i.Description, StringComparison.Ordinal));
        var weapons = Assert.IsType<GraphNode>(Assert.Single(document.ExpandNode("spatial/weapons/0")));
        Assert.Equal(2, weapons.Items.Count);
        foreach (var graph in new[] { skills, weapons })
        {
            SpatialViewValidation.Validate(graph);
            Assert.Empty(graph.Edges);
            Assert.NotEmpty(graph.TextAlternative);
            foreach (var item in graph.Items)
            {
                Assert.Equal(GraphState.Unknown, item.State);
                Assert.Null(item.Cost);
                var target = Assert.IsType<ViewTarget>(item.Target);
                var field = Assert.Single(Resolve(document, target), n => n is FieldNode f && f.ValueId == target.Id);
                Assert.True(Assert.IsType<FieldNode>(field).Spec.ReadOnly);
            }
        }
        var swede = WriteTestData.Table(document).Rows.Single(r => WriteTestData.Field(r, "asset").Value.AsString() == "DA_Item_Shved");
        var asset = WriteTestData.Field(swede, "asset");
        Assert.True(document.ApplyEdits([new(asset.ValueId, FieldValue.Of("DA_Item_ShotgunKS23"))]).StructureChanged);
        var changed = Assert.IsType<GraphNode>(Assert.Single(document.ExpandNode("spatial/weapons/0")));
        Assert.Equal(weapons.Items.Select(i => i.Id), changed.Items.Select(i => i.Id));
        Assert.All(changed.Items, i => Assert.Equal("KS-23", i.Label));
        Assert.True(document.ApplyEdits([new(asset.ValueId, asset.Value)]).Succeeded);
        var restored = Assert.IsType<GraphNode>(Assert.Single(document.ExpandNode("spatial/weapons/0")));
        Assert.Equal(weapons.Items.Select(i => (i.Id, i.Label, i.State)), restored.Items.Select(i => (i.Id, i.Label, i.State)));
    }

    [Fact]
    public void EverySampleMapPageIsBoundedExternalOnlyAndNavigatesToTheMatchingLevelRow()
    {
        using var document = WriteTestData.Plugin.Open(WriteTestData.Sample());
        var markers = new List<MapMarker>();
        for (var start = 0; start < 262; start += AtomicHeartDocument.PageSize)
        {
            var nodes = document.ExpandNode($"spatial/map/{start}");
            var map = Assert.Single(nodes.OfType<MapNode>());
            SpatialViewValidation.Validate(map);
            Assert.NotNull(map.ExternalSource);
            Assert.Null(map.Background);
            Assert.False(map.CoordinateEmbeddingPermitted);
            Assert.InRange(map.Markers.Count, 1, AtomicHeartDocument.PageSize);
            foreach (var marker in map.Markers)
            {
                Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
                Assert.Equal(Confidence.Unknown, marker.Confidence);
                Assert.Null(marker.X);
                Assert.Null(marker.Y);
                Assert.NotNull(marker.ExternalLink);
                var target = Assert.IsType<ViewTarget>(marker.Target);
                var table = Assert.Single(Resolve(document, target).OfType<TableNode>());
                Assert.Single(table.Rows, r => r.Key == target.Id);
            }
            markers.AddRange(map.Markers);
        }
        Assert.Equal(262, markers.Count);
        Assert.Equal(262, markers.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("spatial/map/-1"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("spatial/map/262"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("spatial/detail-pages/999"));
    }

    [Fact]
    public void MissingPacksAndUnmappedSavesRetainHonestSpatialFallbacks()
    {
        using var raw = WriteTestData.Plugin.Open(WriteTestData.Sample());
        var graph = Assert.IsType<GraphNode>(Assert.Single(raw.ExpandNode("spatial/weapons/0")));
        Assert.Equal(2, graph.Items.Count);
        Assert.All(graph.Items, i => Assert.Contains("identifier formatting", i.Description, StringComparison.Ordinal));
        using var synthetic = WriteTestData.Plugin.Open(MemorySaveSet.SingleFile("slot", SlotFixture.Create()));
        Assert.IsType<TextNode>(Assert.Single(synthetic.ExpandNode("spatial/skills/0")));
        Assert.IsType<TextNode>(Assert.Single(synthetic.ExpandNode("spatial/weapons/0")));
        var map = Assert.Single(synthetic.ExpandNode("spatial/map/0").OfType<MapNode>());
        Assert.Single(map.Markers);
        SpatialViewValidation.Validate(map);
    }

    [Fact]
    public void LongIdentifiersDuplicateSkillTagsAndLargeLevelListsStayWithinSpatialLimits()
    {
        var longName = new string('x', RecordIndex.MaxNameUnits);
        var property = new TaggedProperty(longName, "SetProperty", longName, 0, default, new(0, 8), null);
        var record = new ObjectRecord(longName, RecordKinds.Component, "player", longName,
            "/BP_DefaultPlayerSkillTreeComponent.BP_DefaultPlayerSkillTreeComponent_C", "", default, default, [], [property, property], default, []);
        var levels = Enumerable.Range(0, RecordIndex.MaxLevels).Select(i => new LevelRecord("level/" + i, longName, default, 0)).ToArray();
        var index = new RecordIndex(new byte[8], [], [record], levels);
        var spatial = new SpatialViews(index, new InventoryState(index, null), (id, label, spec, get) => new(id, label, spec, get()));
        var graph = Assert.IsType<GraphNode>(Assert.Single(spatial.Expand("spatial/skills/0")));
        SpatialViewValidation.Validate(graph);
        Assert.Equal(2, graph.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count());
        for (var start = 0; start < levels.Length; start += AtomicHeartDocument.PageSize)
        {
            var map = Assert.Single(spatial.Expand($"spatial/map/{start}").OfType<MapNode>());
            SpatialViewValidation.Validate(map);
            Assert.All(map.Markers, m => Assert.True(m.Target!.Id.Length <= SpatialViewLimits.MaxIdLength));
        }
    }

    [Fact]
    public void DuplicateLevelNamesKeepDistinctResolvableIdentitiesWithoutSuffixCollisions()
    {
        string[] names = ["Persistent", "Persistent#2"];
        var bytes = SlotFixture.Create(plain: PlainFixture.Create(sublevelNames: names));
        using var document = WriteTestData.Plugin.Open(MemorySaveSet.SingleFile("slot", bytes));
        var map = Assert.Single(document.ExpandNode("spatial/map/0").OfType<MapNode>());
        Assert.Equal(3, map.Markers.Count);
        Assert.Equal(3, map.Markers.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (var marker in map.Markers)
        {
            var target = marker.Target!;
            var table = Assert.Single(Resolve(document, target).OfType<TableNode>());
            Assert.Single(table.Rows, row => row.Key == target.Id);
        }
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    private static IReadOnlyList<ViewNode> Resolve(ISaveDocument document, ViewTarget target)
    {
        IReadOnlyList<ViewNode> nodes = [document.BuildView()];
        foreach (var part in target.Path)
        {
            if (part.StartsWith("tab:", StringComparison.Ordinal))
                nodes = Assert.Single(nodes.OfType<TabsNode>()).Tabs.Single(t => t.Title == part[4..]).Children;
            else
            {
                var section = Flatten(nodes).OfType<SectionNode>().Single(s => (s.LazyId ?? s.Title) == part[8..]);
                nodes = section.LazyId is { } lazy ? document.ExpandNode(lazy) : section.Children;
            }
        }
        return Flatten(nodes).ToArray();
    }

    private static IEnumerable<ViewNode> Flatten(IReadOnlyList<ViewNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node is SectionNode { LazyId: null } section)
                foreach (var child in Flatten(section.Children)) yield return child;
        }
    }
}
