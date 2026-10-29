using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class SpatialViewTests
{
    [Fact]
    public void SampleEveryRegionalMapPublishesThroughCoreAndEveryTargetResolvesWithoutWrites()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, file.Directory.TitleId);
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("eldenring-spatial", file, plugin, document, references: references);
        var root = Assert.IsType<TabsNode>(session.BuildView());
        var world = root.Tabs.Single(tab => tab.Title == ProgressViews.TabTitle);
        Assert.Single(world.Children.OfType<QueryableTableNode>());
        var slot = Assert.Single(world.Children.OfType<SectionNode>(), section => section.LazyId == "progress/0");
        var regions = session.Expand(slot.LazyId!).OfType<SectionNode>().ToArray();
        Assert.NotEmpty(regions);
        var markers = new HashSet<string>(StringComparer.Ordinal);
        var setCount = 0;
        var positioned = 0;
        foreach (var region in regions)
        {
            var children = session.Expand(region.LazyId!);
            var map = Assert.Single(children.OfType<MapNode>());
            var table = Assert.Single(children.OfType<TableNode>());
            SpatialViewValidation.Validate(map);
            VisualAssetTests.ValidateMap(map, references);
            Assert.Equal(ProgressViews.MapUrl, map.ExternalSource!.Url);
            Assert.Contains("Link only", map.ExternalSource.Attribution);
            Assert.Equal(table.Rows.Count, map.Markers.Count);
            Assert.InRange(map.Markers.Count, 1, 100);
            Assert.Contains("Unknown", map.TextAlternative);
            foreach (var marker in map.Markers)
            {
                Assert.True(markers.Add(marker.Id));
                if (marker.X.HasValue) positioned++;
                Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
                Assert.Equal(Confidence.Inferred, marker.Confidence);
                Assert.Contains("MIT", marker.Description);
                Assert.Contains("not console-validated", marker.Description);
                var target = Assert.IsType<ViewTarget>(marker.Target);
                Assert.Equal(ViewTargetKind.Field, target.Kind);
                Assert.Equal(["tab:World / progress", "section:progress/0", "section:" + region.LazyId,
                    "row:" + marker.Id], target.Path);
                var field = Assert.IsType<FieldNode>(table.Rows.Single(row => row.Key == marker.Id).Cells["raw"]);
                Assert.Equal(target.Id, field.ValueId);
                Assert.True(field.Spec.ReadOnly);
                Assert.Equal(field.Value, document.GetValue(target.Id));
                if (field.Value.AsBool()) setCount++;
            }
            var roundTrip = Assert.IsType<MapNode>(JsonSerializer.Deserialize<ViewNode>(JsonSerializer.Serialize<ViewNode>(map)));
            Assert.Equal(map.Markers.Select(marker => marker.Id), roundTrip.Markers.Select(marker => marker.Id));
        }
        Assert.Equal(499, markers.Count);
        Assert.InRange(positioned, 300, 471);
        Assert.InRange(setCount, 1, 498); // One snapshot contains both raw set and unset candidates, no status claim.
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        WriteTestSupport.EqualBytes(original, document);
    }

    [Fact]
    public void LongImportedEvidenceStaysWithinSpatialTextBudgetAndRemainsCompleteInTable()
    {
        var fixture = new SyntheticSave([0]);
        var character = SemanticReader.Read(fixture.Bytes, EldenRingParser.Parse(fixture.Bytes).Slots[0]);
        var catalog = ProgressReferenceTests.Catalog();
        var original = catalog.Find("grace:ChurchofElleh")!;
        var verbose = original with { Description = new string('x', SpatialViewLimits.MaxTextLength * 2) };
        var views = new ProgressViews(fixture.Bytes, [character], new ProgressTestCatalog(catalog, [verbose]));
        var region = Assert.Single(views.Expand("progress/0", Bind).OfType<SectionNode>());
        var children = views.Expand(region.LazyId!, Bind);
        var map = Assert.Single(children.OfType<MapNode>());
        SpatialViewValidation.Validate(map);
        Assert.Contains("Full evidence", Assert.Single(map.Markers).Description);
        var table = Assert.Single(children.OfType<TableNode>());
        Assert.Contains(verbose.Description, Assert.IsType<FieldNode>(Assert.Single(table.Rows).Cells["raw"]).Description);
    }

    [Fact]
    public void AttributeDependencyGraphRoundTripsAndOnlyNavigatesToExistingReadOnlyFields()
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        var graph = Assert.Single(document.ExpandNode("characters/0").OfType<GraphNode>());
        SpatialViewValidation.Validate(graph);
        Assert.Equal(9, graph.Items.Count);
        Assert.Equal(8, graph.Edges.Count);
        Assert.All(graph.Edges, edge => Assert.Equal("slots/0/level", edge.To));
        Assert.All(graph.Items, item =>
        {
            Assert.Equal(GraphState.Unknown, item.State);
            Assert.Null(item.Cost);
            var target = Assert.IsType<ViewTarget>(item.Target);
            Assert.Equal(["tab:Characters", "section:characters/0"], target.Path);
            Assert.Equal(ViewTargetKind.Field, target.Kind);
            Assert.Equal(FieldValueKind.Integer, document.GetValue(target.Id).Kind);
            Assert.False(document.ApplyEdits([new(target.Id, FieldValue.Of(50L))]).Succeeded);
        });
        var copy = Assert.IsType<GraphNode>(JsonSerializer.Deserialize<ViewNode>(JsonSerializer.Serialize<ViewNode>(graph)));
        Assert.Equal(graph.Items.Select(item => item.Target!.Id), copy.Items.Select(item => item.Target!.Id));
        Assert.Equal(graph.Edges, copy.Edges);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Fact]
    public void SampleInspectionAndQueryingRetainRuneUndoAndNeverMutateProgress()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, file.Directory.TitleId);
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("eldenring-spatial-undo", file, plugin, document, references: references);
        _ = session.BuildView();
        _ = session.Expand("characters/0");
        Assert.Equal("Warrior", session.FindReference("slots/0/class-reference", "1").Label);
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming("slots/0/runes", 1146)]).Ok);
        var edited = WriteTestSupport.Bytes(document);
        var rows = session.QueryCollection("slots/0/progress", new() { Text = "Church of Elleh" });
        var raw = Assert.IsType<FieldNode>(Assert.Single(rows.Rows).Cells["raw"]);
        Assert.False(session.ApplyEdits([WriteTestSupport.Incoming(raw.ValueId, !raw.Value.AsBool())]).Ok);
        foreach (var region in session.Expand("progress/0").OfType<SectionNode>()) _ = session.Expand(region.LazyId!);
        Assert.True(session.State.CanUndo);
        WriteTestSupport.EqualBytes(edited, document);
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(original, document);
        Assert.True(session.Redo().Ok);
        WriteTestSupport.EqualBytes(edited, document);
        Assert.Equal(raw.Value, document.GetValue(raw.ValueId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoPackAndUnsampledLayoutsNeverInventProgress(bool hasPack)
    {
        var plugin = new EldenRingPlugin();
        var fixture = new SyntheticSave([0], version: 151);
        var references = hasPack ? new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169") : null;
        using var document = plugin.Open(fixture.File(), references ?? SaveToolkit.Sdk.References.IReferenceContext.Empty);
        var world = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == ProgressViews.TabTitle);
        Assert.Empty(world.Children.OfType<SectionNode>());
        Assert.DoesNotContain(((IQueryableDocument)document).Collections, collection => collection.Id == "slots/0/progress");
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("progress/0"));
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    private static FieldNode Bind(string id, string label, FieldSpec spec, Func<FieldValue> get) =>
        new(id, label, spec with { ReadOnly = true }, get());
}
