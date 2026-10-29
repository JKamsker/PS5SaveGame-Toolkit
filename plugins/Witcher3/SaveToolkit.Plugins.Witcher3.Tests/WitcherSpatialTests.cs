using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherSpatialTests
{
    [Fact]
    public void EverySamplePublishesAttributedGraphsAndNavigableReadOnlyMapsWithoutChangingBytes()
    {
        var plugin = new Witcher3Plugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA04018");
        foreach (var file in EnabledWriteFixture.Files())
        {
            using var document = plugin.Open(file, references);
            using var session = new DocumentSession("spatial", file, plugin, document, references: references);
            _ = session.BuildView(); // Core validates F9 and reference fields, including every expanded graph.
            var skills = Assert.IsType<GraphNode>(Assert.Single(session.Expand("ability-graph/skills")));
            SpatialViewValidation.Validate(skills);
            Assert.Equal(102, skills.Items.Count);
            Assert.Empty(skills.Edges); // Tiers and positions are not prerequisite edges.
            Assert.All(skills.Items, item => Assert.Equal(GraphState.Unknown, item.State));
            var named = Assert.Single(skills.Items, item => item.Id == "S_Sword_s01");
            Assert.Equal("Whirl", named.Label);
            Assert.Contains("CC-BY-SA-3.0", named.Description!, StringComparison.Ordinal);
            var mutations = Assert.IsType<GraphNode>(Assert.Single(session.Expand("ability-graph/mutations")));
            SpatialViewValidation.Validate(mutations);
            Assert.Equal(13, mutations.Items.Count);
            Assert.Equal(11, mutations.Edges.Count);
            Assert.Contains(new GraphEdge("EPMT_Mutation2", "EPMT_Mutation6"), mutations.Edges);
            Assert.Contains(new GraphEdge("EPMT_Mutation6", "EPMT_Mutation1"), mutations.Edges);
            Assert.All(mutations.Items, item => Assert.Equal(GraphState.Unknown, item.State));
            foreach (var item in skills.Items.Concat(mutations.Items)) { Navigate(document, item.Target!); }
            var map = Assert.IsType<MapNode>(Assert.Single(session.Expand("map-view/0")));
            SpatialViewValidation.Validate(map);
            Assert.Null(map.Background);
            Assert.False(map.CoordinateEmbeddingPermitted);
            Assert.Equal(MapViews.SourceUrl, map.ExternalSource!.Url);
            Assert.Contains("alignment unknown", map.ExternalSource.Attribution, StringComparison.Ordinal);
            var parsed = EnabledWriteFixture.Read(file.ReadAllBytes());
            var tags = MapReader.Read(parsed.Tree, parsed.Container.Payload, parsed.Directory.Names)!;
            Assert.Equal(tags.Count, map.Markers.Count);
            Assert.All(map.Markers, marker => {
                Assert.Null(marker.X);
                Assert.Null(marker.Y);
                Assert.Equal(MapMarkerStatus.Found, marker.Status);
                Navigate(document, marker.Target!);
                Assert.False(document.ApplyEdits([new(marker.Target!.Id, FieldValue.Of("changed"))]).Succeeded);
            });
            var page = session.QueryCollection(MapViews.CollectionId, new() { Limit = 200 });
            Assert.Equal(tags.Count, page.TotalCount);
            Assert.Equal(tags.Select(t => t.Id), page.Rows.Select(r => r.Key));
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void SkillGraphsRefreshAcrossEditUndoAndRedoAndF8SearchFindsUnloadedRows()
    {
        var file = EnabledWriteFixture.Files()[0];
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("graph-history", file, plugin, document);
        _ = session.BuildView();
        var graph = Assert.IsType<GraphNode>(Assert.Single(session.Expand("ability-graph/skills")));
        var item = graph.Items.Single(i => i.Id == "S_Sword_s01");
        foreach (var part in item.Target!.Path.Where(p => p.StartsWith("section:ability/", StringComparison.Ordinal))) { _ = session.Expand(part[8..]); }
        var outcome = session.ApplyEdits([EnabledWriteFixture.Incoming(item.Target!.Id, FieldValue.Of(1))]);
        Assert.True(outcome.Ok);
        Assert.True(outcome.StructureChanged);
        Assert.Equal(GraphState.Unlocked, State());
        Assert.True(session.Undo().Ok);
        Assert.Equal(GraphState.Unknown, State());
        Assert.True(session.Redo().Ok);
        Assert.Equal(GraphState.Unlocked, State());
        var queryable = (IQueryableDocument)document;
        var id = AbilityCollections.Prefix + "skills";
        var last = queryable.Query(id, new() { Offset = 95, Limit = 1 });
        var target = Assert.Single(last.Rows).Key;
        Assert.Equal(target, Assert.Single(queryable.Query(id, new() { Text = target }).Rows).Key);
        Assert.Empty(queryable.Query(id, new() { Offset = int.MaxValue }).Rows);
        Assert.Equal(96, queryable.Query(id, new() { Limit = int.MaxValue }).Rows.Count);
        Assert.Single(queryable.Query(id, new() { Filters = new Dictionary<string, string> { ["id"] = item.Id } }).Rows);
        Assert.Throws<ArgumentException>(() => queryable.Query(id, new() { SortColumn = "unsupported" }));
        Assert.Throws<ArgumentException>(() => queryable.Query(id, new() { Cursor = "unsupported" }));

        GraphState State() => Assert.IsType<GraphNode>(Assert.Single(session.Expand("ability-graph/skills"))).Items.Single(i => i.Id == item.Id).State;
    }

    [Fact]
    public void MalformedMutationRequirementsWarnWithoutBreakingDefinitionTables()
    {
        string[] names = ["a", "b"];
        TaggedProperty Prerequisites(byte[] bytes, string type = "array:2,0,EPlayerMutationType") =>
            new("requiredMutations", type, 0, new byte[8].Concat(bytes).ToArray());
        var a = new AbilityEntry("a", new TaggedRecord([Prerequisites([1, 0, 0, 0, 2, 0])]));
        var b = new AbilityEntry("b", new TaggedRecord([]));
        GraphNode Graph(params AbilityEntry[] entries) => Assert.IsType<GraphNode>(Assert.Single(
            new AbilityGraphs([new("mutations", entries)], names, null, null).Build("mutations")));
        Assert.Equal(new GraphEdge("b", "a"), Assert.Single(Graph(a, b).Edges));
        void Warn(params AbilityEntry[] entries) => Assert.Equal(Tone.Warning, Assert.IsType<TextNode>(Assert.Single(
            new AbilityGraphs([new("mutations", entries)], names, null, null).Build("mutations"))).Tone);
        Warn(a); // Missing prerequisite endpoint.
        Warn(a, b with { Record = new TaggedRecord([Prerequisites([1, 0, 0, 0, 1, 0])]) }); // Cycle.
        Warn(a with { Record = new TaggedRecord([Prerequisites([2, 0, 0, 0, 2, 0, 2, 0])]) }, b); // Duplicate.
        Warn(a with { Record = new TaggedRecord([Prerequisites([255, 255, 255, 127])]) }, b); // Excessive count.
        Warn(a with { Record = new TaggedRecord([Prerequisites([1, 0, 0, 0, 2, 0, 0])]) }, b); // Trailing byte.
        Warn(a with { Record = new TaggedRecord([Prerequisites([1, 0, 0, 0, 2, 0], "wrong")]) }, b);
    }

    [Fact]
    public void MapReaderSeparatesMembershipsAndRejectsMalformedCountsNamesTypesAndDuplicates()
    {
        var (manager, nodes, bytes) = MapFixture();
        var tags = MapReader.ReadTags(manager, nodes, bytes, ["known", "discovered", "disabled"]);
        Assert.Equal(new SavedMapTag("known", true, false, false), tags.Single(t => t.Id == "known"));
        Assert.Equal(new SavedMapTag("discovered", false, true, false), tags.Single(t => t.Id == "discovered"));
        Assert.Equal(new SavedMapTag("disabled", false, false, true), tags.Single(t => t.Id == "disabled"));
        foreach (var value in new[] { -1, 0, int.MaxValue })
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(nodes[2].ValueOffset), value);
            Assert.Throws<SaveFormatException>(() => MapReader.ReadTags(manager, nodes, bytes, ["known", "discovered", "disabled"]));
        }
        (manager, nodes, bytes) = MapFixture();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(nodes[3].ValueOffset), 0);
        Assert.Throws<SaveFormatException>(() => MapReader.ReadTags(manager, nodes, bytes, ["known", "discovered", "disabled"]));
        (manager, nodes, bytes) = MapFixture();
        nodes[3] = nodes[3] with { Type = "Int32" };
        Assert.Throws<SaveFormatException>(() => MapReader.ReadTags(manager, nodes, bytes, ["known", "discovered", "disabled"]));
        (manager, nodes, bytes) = MapFixture();
        nodes[1].Children.Add(3);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(nodes[2].ValueOffset), 2);
        Assert.Throws<SaveFormatException>(() => MapReader.ReadTags(manager, nodes, bytes, ["known", "discovered", "disabled"]));
    }

    [Fact]
    public void LargeMapListsArePagedAndEveryMarkerTargetsAnUnloadedDetailPage()
    {
        var (manager, nodes, bytes) = MapFixture(550);
        var container = WitcherContainer.Parse(SyntheticSave.Create());
        var tree = Sav3Tree.Parse(container.Payload, Sav3Directory.Parse(container));
        tree.Nodes.Clear();
        tree.Nodes.AddRange(nodes);
        string[] names = Enumerable.Range(1, 1650).Select(i => $"tag-{i:D4}").ToArray();
        var values = new Dictionary<string, FieldValue>();
        FieldNode Bind(string id, string label, FieldSpec spec, Func<FieldValue> get, Action<FieldValue>? set, Func<FieldValue, string?>? validate)
        {
            values[id] = get();
            return new(id, label, spec with { ReadOnly = true }, get());
        }
        var views = new MapViews(tree, bytes, names, Bind);
        var seen = new HashSet<string>();
        for (var page = 0; page < 4; page++)
        {
            var map = Assert.IsType<MapNode>(Assert.Single(views.Expand($"map-view/{page}")));
            SpatialViewValidation.Validate(map);
            Assert.InRange(map.Markers.Count, 1, 512);
            foreach (var marker in map.Markers)
            {
                Assert.True(seen.Add(marker.Id));
                var target = marker.Target!;
                Assert.Equal(3, target.Path.Count);
                _ = views.Expand(target.Path[1][8..]);
                Assert.True(values.ContainsKey(target.Id));
            }
        }
        Assert.Equal(1650, seen.Count);
        Assert.Equal("tag-1650", Assert.Single(views.Query(new() { Text = "tag-1650", Limit = 1 }).Rows).Key);
        Assert.Equal(200, views.Query(new() { Limit = int.MaxValue }).Rows.Count);
        Assert.Empty(views.Query(new() { Offset = int.MaxValue }).Rows);
        Assert.Throws<ArgumentException>(() => views.Query(new() { Filters = new Dictionary<string, string> { ["invalid"] = "x" } }));
        Assert.Throws<KeyNotFoundException>(() => views.Expand("map-view/2147483647"));
        Assert.Throws<KeyNotFoundException>(() => views.Expand("map-details/-1"));
    }

    private static void Navigate(ISaveDocument document, ViewTarget target)
    {
        Assert.Equal(ViewTargetKind.Field, target.Kind);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        IReadOnlyList<ViewNode> nodes = root.Tabs.Single(t => "tab:" + t.Title == target.Path[0]).Children;
        foreach (var part in target.Path.Skip(1))
        {
            var key = part[(part.IndexOf(':') + 1)..];
            if (part.StartsWith("section:", StringComparison.Ordinal))
            {
                var section = Assert.Single(nodes.OfType<SectionNode>(), s => s.LazyId == key || s.Title == key);
                nodes = section.LazyId is { } lazy ? document.ExpandNode(lazy) : section.Children;
            }
            else
            {
                var row = Assert.Single(nodes.OfType<TableNode>().SelectMany(t => t.Rows), r => r.Key == key);
                nodes = row.Cells.Values.ToArray();
            }
        }
        var field = Assert.Single(nodes.OfType<FieldNode>(), f => f.ValueId == target.Id);
        Assert.Equal(field.Value, document.GetValue(target.Id));
    }

    private static (Sav3Node Manager, List<Sav3Node> Nodes, byte[] Bytes) MapFixture(int count = 1)
    {
        var nodes = new List<Sav3Node>();
        var bytes = new byte[(count * 3 + 10) * sizeof(int)];
        Sav3Node Add(string name, string tag, string? type = null, int value = 0, int width = 4)
        {
            var offset = nodes.Count * 4;
            var node = new Sav3Node(nodes.Count, offset, width, tag, name, type, offset, width);
            nodes.Add(node);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
            return node;
        }
        var manager = Add("CCommonMapManager", "BS");
        foreach (var (name, value) in new[] { ("KnownMapPinTags", 1), ("DiscoveredMapPinTags", 2), ("DisabledMapPinTags", 3) })
        {
            var block = Add(name, "BS");
            block.Children.Add(Add("Size", "VL", "Uint32", count).Id);
            for (var i = 0; i < count; i++) { block.Children.Add(Add("MapPinTag", "VL", "CName", (value - 1) * count + i + 1, 2).Id); }
            manager.Children.Add(block.Id);
        }
        return (manager, nodes, bytes);
    }
}
