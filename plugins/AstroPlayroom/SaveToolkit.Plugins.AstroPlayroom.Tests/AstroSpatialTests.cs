using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroSpatialTests
{
    [Fact]
    public void SampleMapsHaveExactStableRecordsAttributionNavigationAndNoInventedPhysicalPositions()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        foreach (var file in AstroTestData.Samples())
        {
            using var document = plugin.Open(file, context);
            var levels = Assert.Single(document.ExpandNode("astro-map:World:0").OfType<MapNode>());
            SpatialViewValidation.Validate(levels);
            Assert.NotEmpty(levels.Markers);
            Assert.True(levels.CoordinateEmbeddingPermitted);
            Assert.Contains("original layout", levels.CoordinateAttribution!.License, StringComparison.Ordinal);
            Assert.All(levels.Markers, marker =>
            {
                Assert.NotNull(marker.X);
                Assert.NotNull(marker.Y);
                Assert.Equal(ViewTargetKind.Field, marker.Target!.Kind);
                Assert.Equal(["tab:Progress"], marker.Target.Path);
                Assert.Equal(document.GetValue(marker.Target.Id).AsBool(), marker.Status == MapMarkerStatus.Found);
            });

            var markers = new List<MapMarker>();
            foreach (var (group, title, count) in new[] { ("Artifact", "Artifacts", 50), ("Puzzle", "Puzzle pieces", 96), ("Prop", "Props", 18) })
            {
                var maps = ReadMaps(document, group);
                Assert.Equal(count, maps.Sum(map => map.Markers.Count));
                Assert.All(maps, AssertCollectionMap);
                var rows = AstroCatalogueTestData.Table(document, title).Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);
                foreach (var marker in maps.SelectMany(map => map.Markers))
                {
                    Assert.Null(marker.X);
                    Assert.Null(marker.Y);
                    Assert.Contains("CC-BY-SA-3.0", marker.Description, StringComparison.Ordinal);
                    Assert.NotNull(marker.Target);
                    Assert.Equal(ViewTargetKind.Row, marker.Target.Kind);
                    Assert.Equal(["tab:Collectibles", $"section:{title}"], marker.Target.Path);
                    Assert.True(rows.ContainsKey(marker.Target.Id));
                    Assert.Equal(marker.Id, marker.Target.Id);
                }
                markers.AddRange(maps.SelectMany(map => map.Markers));
            }
            Assert.Equal(164, markers.Select(marker => marker.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(file.Directory.Generation == 0 ? 0 : 13, markers.Count(marker => marker.Status == MapMarkerStatus.Found));
            Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        }
    }

    private static void AssertCollectionMap(MapNode map)
    {
        SpatialViewValidation.Validate(map);
        Assert.Null(map.Background);
        Assert.False(map.CoordinateEmbeddingPermitted);
        Assert.Contains("PowerPyx", map.ExternalSource!.Attribution, StringComparison.Ordinal);
        Assert.NotEmpty(map.TextAlternative);
    }

    [Fact]
    public void MapsRefreshThroughCoreEditsUndoAndRedoAndNavigationDoesNotMutate()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples()[0];
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("astro-spatial", Assert.IsType<DiskSaveFile>(file), plugin, document, references: context);
        _ = session.BuildView();
        var before = Assert.Single(session.Expand("astro-map:Artifact:0").OfType<MapNode>());
        var marker = before.Markers[0];
        var row = AstroCatalogueTestData.Table(document, "Artifacts").Rows.Single(row => row.Key == marker.Target!.Id);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        var id = row.Key + "/IsGot[0]/text[0]";
        foreach (var ancestor in new[] { "/PreInstall[0]", "/PreInstall[0]/Artifact[0]", row.Key, row.Key + "/IsGot[0]" })
            _ = session.Expand(ancestor);
        Assert.True(session.ApplyEdits([new IncomingEdit(id, System.Text.Json.JsonSerializer.SerializeToElement(true))]).StructureChanged);
        Check(MapMarkerStatus.Found);
        Assert.True(session.Undo().Ok);
        Check(MapMarkerStatus.Missing);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Check(MapMarkerStatus.Found);

        void Check(MapMarkerStatus status) => Assert.Equal(status,
            Assert.Single(session.Expand("astro-map:Artifact:0").OfType<MapNode>()).Markers.Single(item => item.Id == marker.Id).Status);
    }

    [Fact]
    public void LargeUnknownCataloguesPageMapsAndTablesAndUseRawTargetsForUnloadedRows()
    {
        var text = "\r\n<PreInstall><Artifact>" + string.Concat(Enumerable.Range(0, 550)
            .Select(index => $"<item_{index}><IsGot>false</IsGot></item_{index}>")) + "</Artifact></PreInstall>";
        using var document = AstroTestData.Open(AstroTestData.Bytes(text));
        var maps = ReadMaps(document, "Artifact");
        Assert.Equal(9, maps.Count);
        Assert.Equal(550, maps.Sum(map => map.Markers.Count));
        Assert.All(maps, map =>
        {
            Assert.InRange(map.Markers.Count, 1, 64);
            SpatialViewValidation.Validate(map);
            Assert.All(map.Markers, marker =>
            {
                Assert.Equal(Confidence.Unknown, marker.Confidence);
                Assert.Equal(ViewTargetKind.Field, marker.Target!.Kind);
                Assert.Equal("tab:Raw", marker.Target.Path[0]);
                Assert.Equal(FieldValue.Of(false), document.GetValue(marker.Target.Id));
            });
        });
        Assert.Single(AstroCatalogueTestData.Group(document, "Artifacts").Children.OfType<QueryableTableNode>());
        Assert.Empty(AstroCatalogueTestData.Group(document, "Artifacts").Children.OfType<TableNode>());
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("astro-map:Artifact:2147483647"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("astro-map:Artifact:-1"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("astro-map:Other:0"));
        Assert.Equal(AstroTestData.Bytes(text), AstroTestData.Output(document));
    }

    [Fact]
    public void OversizedUnknownPathsRemainInRawWithoutBreakingF9Publication()
    {
        var key = new string('x', 260);
        var text = $"\r\n<PreInstall><Artifact><{key}><IsGot>false</IsGot></{key}></Artifact></PreInstall>";
        using var document = AstroTestData.Open(AstroTestData.Bytes(text));
        var map = Assert.Single(ReadMaps(document, "Artifact"));
        SpatialViewValidation.Validate(map);
        Assert.Empty(map.Markers);
        Assert.Contains(AstroTestData.Fields(document), field => field.Description == $"PreInstall/Artifact/{key}/IsGot");
        Assert.Equal(AstroTestData.Bytes(text), AstroTestData.Output(document));
    }

    [Fact]
    public void UnknownOwnershipIsNotMissingAndNonBooleanWorldFlagsDoNotCrashViews()
    {
        const string text = "\r\n<PreInstall><Common><NumCoin>one</NumCoin><NumCoin>2</NumCoin></Common>" +
            "<World><A><IsCleared>2</IsCleared><IsVisited>broken</IsVisited></A></World>" +
            "<Artifact><unknown><Mystery>keep</Mystery></unknown></Artifact></PreInstall>";
        using var document = AstroTestData.Open(AstroTestData.Bytes(text));
        _ = document.BuildView();
        var marker = Assert.Single(Assert.Single(ReadMaps(document, "Artifact")).Markers);
        Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
        var section = Assert.Single(AstroCatalogueTestData.Catalogue(document).Children.OfType<SectionNode>(), section => section.Title == "Candidate-area progress");
        var stats = Assert.IsType<StatsNode>(section.Children[0]);
        Assert.Equal("0", Assert.Single(stats.Items, stat => stat.Label == "Missing ownership flags").Value);
        Assert.Equal("1", Assert.Single(stats.Items, stat => stat.Label == "Unknown ownership").Value);
        var plan = Assert.IsAssignableFrom<IActionPlanningDocument>(document).PlanAction("common/set-coins",
            new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(1L) }, new());
        Assert.NotEmpty(plan.Errors);
        Assert.Equal(AstroTestData.Bytes(text), AstroTestData.Output(document));
    }

    internal static IReadOnlyList<MapNode> ReadMaps(ISaveDocument document, string group)
    {
        var maps = new List<MapNode>();
        var lazyId = $"astro-map:{group}:0";
        while (lazyId is not null)
        {
            var nodes = document.ExpandNode(lazyId);
            maps.Add(Assert.Single(nodes.OfType<MapNode>()));
            lazyId = nodes.OfType<SectionNode>().SingleOrDefault()?.LazyId;
        }
        return maps;
    }
}
