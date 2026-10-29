using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class SpatialReferenceTests
{
    private static readonly string[] MissionPath = ["tab:Quests", "section:Mission reference catalogue"];
    [Fact]
    public void EverySampleHasUnknownMissionGraphAndAtlasWithoutInventedSaveLocations()
    {
        var plugin = new PragmataPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        Assert.Equal(43, context.Catalog(ReferenceReadouts.MissionsCatalog)!.Count);
        foreach (var file in SampleEvidenceTests.Files().Where(file => file.FileName != "data00-1.bin"))
        {
            using var document = (PragmataDocument)plugin.Open(file, context);
            var tabs = ((TabsNode)document.BuildView()).Tabs;
            var quests = tabs.Single(tab => tab.Title == "Quests");
            AssertMissionGraph(document, quests);
            AssertCheckpointMap(document, tabs.Single(tab => tab.Title == "World"));
            Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        }
    }

    private static void AssertMissionGraph(PragmataDocument document, Tab quests)
    {
        var graph = Assert.Single(quests.Children.OfType<GraphNode>());
        SpatialViewValidation.Validate(graph);
        Assert.Equal(24, graph.Items.Count);
        Assert.Equal(18, graph.Edges.Count);
        var fields = quests.Children.OfType<SectionNode>().Where(section => section.LazyId is null)
            .SelectMany(section => section.Children).OfType<FieldNode>().ToDictionary(field => field.ValueId);
        Assert.All(graph.Items, item =>
        {
            Assert.Equal(GraphState.Unknown, item.State);
            Assert.Contains("MIT", item.Description!, StringComparison.Ordinal);
            Assert.Contains("Egg Targaryen", item.Description!, StringComparison.Ordinal);
            var target = Assert.IsType<ViewTarget>(item.Target);
            Assert.Equal(ViewTargetKind.Field, target.Kind);
            Assert.True(fields[target.Id].Spec.ReadOnly);
            Assert.Equal(fields[target.Id].Value, document.GetValue(target.Id));
            Assert.Equal(MissionPath, target.Path);
            Assert.False(document.ApplyEdits([new(target.Id, fields[target.Id].Value)]).Succeeded);
        });
    }

    private static void AssertCheckpointMap(PragmataDocument document, Tab world)
    {
        var mapSection = world.Children.OfType<SectionNode>().Single(section => section.LazyId!.StartsWith("spatial/", StringComparison.Ordinal));
        var nodes = document.ExpandNode(mapSection.LazyId!);
        var map = Assert.Single(nodes.OfType<MapNode>());
        SpatialViewValidation.Validate(map);
        Assert.NotEmpty(map.Markers);
        Assert.Equal(CommunityAtlas.BackgroundId, map.Background!.AssetId);
        Assert.True(map.Background.EmbeddingPermitted);
        Assert.NotNull(map.CoordinateAttribution);
        Assert.True(map.CoordinateEmbeddingPermitted);
        Assert.Equal(CheckpointMap.ExternalMapUrl, map.ExternalSource!.Url);
        var controls = nodes.OfType<SectionNode>().SelectMany(section => section.Children).OfType<FieldNode>().ToDictionary(field => field.ValueId);
        Assert.All(map.Markers.Where(marker => marker.Id.StartsWith("checkpoint/", StringComparison.Ordinal)), marker =>
        {
            Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
            Assert.Equal(Confidence.Unknown, marker.Confidence);
            Assert.Null(marker.X); Assert.Null(marker.Y);
            Assert.Equal(document.GetValue(marker.Target!.Id), controls[marker.Target.Id].Value);
            Assert.Equal(new[] { "tab:World", $"section:{mapSection.LazyId}", "section:Checkpoint values" }, marker.Target.Path);
            Assert.Contains("Unknown", marker.Description!, StringComparison.Ordinal);
            Assert.Contains(HashNames.Source, marker.Description!, StringComparison.Ordinal);
        });
        var locations = map.Markers.Where(marker => marker.Id.StartsWith("community/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(26, locations.Length);
        Assert.All(locations, marker =>
        {
            Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
            Assert.Equal(Confidence.Inferred, marker.Confidence);
            Assert.Null(marker.Target);
            Assert.NotNull(marker.X); Assert.NotNull(marker.Y);
            Assert.InRange(marker.X.Value * map.Transform.ScaleX, map.Bounds.MinX, map.Bounds.MaxX);
            Assert.InRange(marker.Y.Value * map.Transform.ScaleY, map.Bounds.MinY, map.Bounds.MaxY);
            Assert.Contains("Unknown", marker.Description!, StringComparison.Ordinal);
            Assert.Contains("Itemaps", marker.Description!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BrowseOnlyMissionReferencesFilterSortAndRejectWritesWithoutChangingHistory()
    {
        var plugin = new PragmataPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(file => file.FileName == "data000.bin");
        using var document = (PragmataDocument)plugin.Open(file, context);
        using var session = new DocumentSession("mission-browse", file, plugin, document, references: context);
        var quests = ((TabsNode)session.BuildView()).Tabs.Single(tab => tab.Title == "Quests");
        var field = quests.Children.OfType<SectionNode>().Where(section => section.LazyId is null)
            .SelectMany(section => section.Children).OfType<FieldNode>()
            .First(field => ((ReferenceSpec)field.Spec).CatalogId == ReferenceReadouts.MissionsCatalog);
        Assert.True(field.Spec.ReadOnly);
        var catalogue = context.Catalog(ReferenceReadouts.MissionsCatalog)!;
        var category = catalogue.Find(field.Value.AsString())!.Category;
        var query = new ReferenceQuery { Category = category, SortBy = ReferenceSortKey.Name, Descending = true, Limit = 200 };
        var page = session.SearchReferences(field.ValueId, query, session.State.Revision);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, candidate => Assert.Equal(category, candidate.Category));
        Assert.Equal(page.Items.Select(candidate => candidate.Label).OrderDescending(StringComparer.OrdinalIgnoreCase), page.Items.Select(candidate => candidate.Label));
        Assert.Contains(ReferenceSortKey.Name, page.SortKeys);
        Assert.False(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(page.Items[0].RawId))]).Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
    }

    [Fact]
    public void ReadOnlyMissionInspectionKeepsCurrentValuesRevisionAndUndoRedoHistory()
    {
        var plugin = new PragmataPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(file => file.FileName == "data000.bin");
        using var document = (PragmataDocument)plugin.Open(file, context);
        using var session = new DocumentSession("mission-inspect", file, plugin, document, references: context);
        var action = ((TabsNode)session.BuildView()).Tabs.Single(tab => tab.Title == "Quests").Children.OfType<ActionNode>().Single();
        Assert.False(action.Mutates);
        Assert.False(action.SupportsPlanning);
        Assert.True(session.ApplyEdits([new(EditingTests.Quantity(document).Path, JsonSerializer.SerializeToElement("17"))]).Ok);
        var before = document.Serialize().Single().Content.ToArray();
        var state = session.State;
        var mission = action.Parameters[0].Value.AsString();
        var result = session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement> { ["mission"] = JsonSerializer.SerializeToElement(mission) }, state.Revision);
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("Egg Targaryen", result.Message!, StringComparison.Ordinal);
        Assert.Empty(result.Changes);
        Assert.Equal(state, session.State);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.False(session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement> { ["mission"] = JsonSerializer.SerializeToElement("unknown") }).Ok);
    }

    [Theory]
    [InlineData(201, 200)]
    [InlineData(1_000_000, 999_999)]
    public void LargeMapNavigationPathsOpenExactlyTheContainingBoundedLeaf(int count, int index)
    {
        var pages = new LazyPages();
        var root = pages.Section("test", "Test map", count,
            (start, length) => Enumerable.Range(start, length).Select(value => (ViewNode)new TextNode(value.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());
        IReadOnlyList<ViewNode> nodes = [root];
        foreach (var part in LazyPages.NavigationPath("test", count, index))
            nodes = pages.Expand(nodes.OfType<SectionNode>().Single(section => $"section:{section.LazyId}" == part).LazyId!);
        Assert.InRange(nodes.Count, 1, LazyPages.PageSize);
        Assert.Contains(nodes.OfType<TextNode>(), node => node.Text == index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
