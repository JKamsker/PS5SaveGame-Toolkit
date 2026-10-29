using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class SpatialViewTests
{
    [Theory]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void AllPrimaryRegionsAndGraphsHaveValidLocalTargetsAndPreserveBytes(string title)
    {
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(file => !file.Directory.IsBackup &&
            Ff7Schema.ForTitle(title)!.TryGetRole(file.FileName, out var common) && !common))
        {
            var original = file.ReadAllBytes();
            using var document = new FinalFantasy7Plugin().Open(file);
            var tabs = (TabsNode)document.BuildView();
            var regions = tabs.Tabs.Single(tab => tab.Title == RegionalMaps.TabTitle).Children.OfType<SectionNode>().ToArray();
            Assert.Equal(6, regions.Length);
            var markers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in regions)
            {
                var expanded = document.ExpandNode(section.LazyId!);
                var map = Assert.IsType<MapNode>(expanded[0]);
                SpatialViewValidation.Validate(map);
                Assert.Null(map.Background);
                Assert.Equal(RegionalMaps.ExternalMapUrl, map.ExternalSource!.Url);
                var fields = Fields(expanded).ToDictionary(field => field.ValueId);
                foreach (var marker in map.Markers)
                {
                    Assert.True(markers.Add(marker.Id));
                    Assert.Null(marker.X);
                    Assert.Null(marker.Y);
                    Assert.Equal(Confidence.Inferred, marker.Confidence);
                    var field = fields[marker.Target!.Id];
                    Assert.Equal(field.Value, document.GetValue(field.ValueId));
                    Assert.Equal(field.Value.AsInt32() == 1 ? MapMarkerStatus.Found : MapMarkerStatus.Missing, marker.Status);
                    Assert.Contains("tab:" + RegionalMaps.TabTitle, marker.Target.Path);
                }
                Assert.IsType<MapNode>(JsonSerializer.Deserialize<ViewNode>(JsonSerializer.Serialize<ViewNode>(map)));
            }
            Assert.NotEmpty(markers);

            var folioBits = new HashSet<string>(StringComparer.Ordinal);
            for (var character = 0; character < Ff7Schema.CharacterCount; character++)
            {
                foreach (var kind in new[] { "sockets", "folio" })
                {
                    var expanded = document.ExpandNode($"spatial/{kind}/{character}");
                    var graph = Assert.IsType<GraphNode>(expanded[0]);
                    SpatialViewValidation.Validate(graph);
                    Assert.Empty(graph.Edges); // Source supplies no verified prerequisites or socket links.
                    var fields = Fields(expanded).ToDictionary(field => field.ValueId);
                    Assert.All(graph.Items, item =>
                    {
                        Assert.True(fields[item.Target!.Id].Spec.ReadOnly);
                        Assert.Equal(fields[item.Target.Id].Value, document.GetValue(item.Target.Id));
                        Assert.Contains("MIT", item.Description);
                        if (kind == "folio") Assert.True(folioBits.Add(item.Id));
                        else Assert.Equal(GraphState.Unknown, item.State);
                    });
                    if (kind == "sockets") Assert.Equal(17, graph.Items.Count);
                    Assert.IsType<GraphNode>(JsonSerializer.Deserialize<ViewNode>(JsonSerializer.Serialize<ViewNode>(graph)));
                }
            }
            Assert.Equal(238, folioBits.Count);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void PublishedMapRefreshesAfterEditUndoAndRedoThroughCore()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var plugin = new FinalFantasy7Plugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("map", file, plugin, document);
        _ = session.BuildView();
        var nodes = session.Expand("spatial/map/0");
        var marker = ((MapNode)nodes[0]).Markers[0];
        var field = Fields(nodes).Single(field => field.ValueId == marker.Target!.Id);
        var original = file.ReadAllBytes();
        var result = session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(1 - field.Value.AsInt32()))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Ok);
        Assert.True(result.StructureChanged);
        var changed = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(1, ByteDiffer.Compare(original, changed).ChangedBytes);
        Assert.NotEqual(marker.Status, ((MapNode)session.Expand("spatial/map/0")[0]).Markers[0].Status);
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(marker.Status, ((MapNode)session.Expand("spatial/map/0")[0]).Markers[0].Status);
        Assert.True(session.Redo().Ok);
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UnsupportedSpatialIdsAndReadonlyFolioEditsRejectWithoutMutation()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        using var document = new FinalFantasy7Plugin().Open(file);
        foreach (var id in new[] { "spatial/map/6", "spatial/sockets/7", "spatial/folio/7", "spatial/folio/-1", "spatial/unknown/0" })
            Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(id));
        var field = Fields(document.ExpandNode("spatial/folio/0")).First();
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        using var remake = new FinalFantasy7Plugin().Open(SampleSaves.Load("PPSA01911").AllDiskFiles.First(file => file.FileName == "ff7remake000"));
        Assert.Throws<KeyNotFoundException>(() => remake.ExpandNode("spatial/folio/0"));
    }

    private static IEnumerable<FieldNode> Fields(IEnumerable<ViewNode> nodes) => nodes.SelectMany(node => node switch
    {
        FieldNode field => [field],
        SectionNode section => Fields(section.Children),
        TableNode table => Fields(table.Rows.SelectMany(row => row.Cells.Values)),
        _ => Enumerable.Empty<FieldNode>(),
    });
}
