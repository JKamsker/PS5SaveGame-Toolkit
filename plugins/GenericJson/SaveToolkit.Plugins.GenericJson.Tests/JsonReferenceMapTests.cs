using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonReferenceMapTests
{
    private const string Settings = """
        {"version":136,"data":[{"group_name":"/video/display","options":[
        {"name":"FrameRateModes","type":"string_list","value":"Quality","index":1,"default_index":0,"values":["Performance","Quality"]}]}]}
        """;

    [Fact]
    public void AttributedPickersBrowseFilterSortAndPlanAtomicRawChoiceAndIndexWrites()
    {
        using var fixture = new JsonSessionFixture(Settings, title: "PPSA04027", fileName: "settings.json", useReferences: true);
        var action = JsonSemanticTestHelper.Actions(fixture).Single(a => a.ActionId.EndsWith("/set", StringComparison.Ordinal));
        var parameter = Assert.Single(action.Parameters);
        Assert.IsType<EnumSpec>(parameter.Spec); // Shared F6 dialog lacks reference-picker context; browse field below uses F4.
        Assert.Contains("CD PROJEKT RED", parameter.Description);
        Assert.Contains("owner decision 2026-10-29", parameter.Description);
        var browse = Assert.Single(fixture.Fields().DistinctBy(f => f.ValueId), f => f.ValueId.StartsWith("catalogue/", StringComparison.Ordinal));
        Assert.True(browse.Spec.ReadOnly);
        Assert.Equal("cp2077-v136-display", Assert.IsType<ReferenceSpec>(browse.Spec).CatalogId);
        var page = fixture.Session.SearchReferences(browse.ValueId,
            new() { Category = "display mode", SortBy = ReferenceSortKey.Name, Descending = true, Limit = 1 }, fixture.Session.State.Revision);
        Assert.Equal(2, page.Total);
        Assert.Equal("Quality", Assert.Single(page.Items).RawId);
        Assert.NotNull(page.Items[0].Dataset);
        Assert.Equal("Quality", fixture.Document.GetValue(browse.ValueId).AsString());
        Assert.Single(fixture.Session.SearchReferences(browse.ValueId, new() { Text = "Performance" }).Items);
        Assert.False(fixture.Edit(browse.ValueId, FieldValue.Of("Performance")).Ok);
        var invalid = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.Of("Unknown"))),
            expectedRevision: fixture.Session.State.Revision);
        Assert.NotEmpty(invalid.Plan.Errors);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.Of("Performance")));
        Assert.Equal("Quality", fixture.Document.GetValue(browse.ValueId).AsString());
        var review = fixture.Session.GetChanges();
        Assert.True(review.Complete);
        Assert.Equal(2, review.Changes.Count);
        Assert.DoesNotContain(review.Changes, c => c.ValueId.StartsWith("catalogue/", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingPackKeepsRecordedEnumAndPackUseIsPinnedToVersionAndTitle()
    {
        using var fixture = new JsonSessionFixture(Settings, title: "PPSA04027", fileName: "settings.json");
        var action = JsonSemanticTestHelper.Actions(fixture).Single(a => a.ActionId.EndsWith("/set", StringComparison.Ordinal));
        Assert.IsType<EnumSpec>(Assert.Single(action.Parameters).Spec);
        var plugin = new GenericJsonPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        Assert.Null(store.Pin(plugin, "PPSA02343").Catalog("cp2077-v136-display", "136"));
        var context = store.Pin(plugin, "PPSA04027");
        Assert.Null(context.Catalog("cp2077-v136-display", "137"));
        var catalog = context.Catalog("cp2077-v136-difficulty", "136");
        Assert.NotNull(catalog);
        Assert.Equal(4, catalog.Count);
        Assert.Contains(context.Datasets(), d => d.Used);
    }

    [Theory]
    [InlineData("PPSA04027", "settings.json")]
    [InlineData("PPSA02343", "ue4savegame.dpx.sav")]
    public void RealSamplesPublishAttributedPickersOrValidExternalMapsWithResolvableTargets(string title, string name)
    {
        var plugin = new GenericJsonPlugin();
        var store = new ReferencePackStore([plugin]);
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName == name))
        {
            var context = store.Pin(plugin, title);
            using var document = plugin.Open(file, context);
            var tabs = Assert.IsType<TabsNode>(document.BuildView());
            if (title == "PPSA04027")
            {
                var fields = JsonEditingTests.Fields(document);
                Assert.Equal(2, fields.DistinctBy(f => f.ValueId).Count(f => f.Spec is ReferenceSpec));
                Assert.Equal(2, context.Datasets().Count(d => d.Used));
            }
            else
            {
                var map = Assert.Single(tabs.Tabs.SelectMany(t => t.Children).OfType<MapNode>());
                SpatialViewValidation.Validate(map);
                Assert.NotEmpty(map.Markers);
                Assert.Null(map.Background);
                Assert.Contains("powerpyx.com", map.ExternalSource!.Url);
                foreach (var marker in map.Markers)
                {
                    Assert.Null(marker.X);
                    Assert.Null(marker.Y);
                    Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
                    Assert.NotNull(marker.Target);
                    AssertTarget(document, tabs, marker.Target);
                }
            }
            Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
        }
    }

    [Fact]
    public void MapTargetsNavigateWideRootRangesWithoutInventingPrerequisites()
    {
        var prefix = string.Join(',', Enumerable.Range(0, 20_000).Select(i => $"\"unknown{i}\":0"));
        var source = "{" + prefix + ",\"Profile\":{\"Minigame.Example\":\"true\"},\"CloudSettings\":{},\"LocalSettings\":{}}";
        using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("ue4savegame.dpx.sav", Encoding.UTF8.GetBytes(source), "PPSA02343"));
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var map = Assert.Single(tabs.Tabs.SelectMany(t => t.Children).OfType<MapNode>());
        SpatialViewValidation.Validate(map);
        AssertTarget(document, tabs, Assert.Single(map.Markers).Target!);
        Assert.DoesNotContain(tabs.Tabs.SelectMany(t => t.Children), n => n is GraphNode);
    }

    [Fact]
    public void MapIdentityUsesInternalGameKeyAcrossPropertyReordering()
    {
        const string First = "{\"Profile\":{\"Minigame.Example\":\"true\",\"MinigameCounter.Example_MayHighScore\":\"7\"},\"CloudSettings\":{},\"LocalSettings\":{}}";
        const string Second = "{\"LocalSettings\":{},\"Profile\":{\"MinigameCounter.Example_MayHighScore\":\"7\",\"Minigame.Example\":\"true\"},\"CloudSettings\":{}}";
        Assert.Equal(Identity(First), Identity(Second));

        static string Identity(string source)
        {
            using var document = new GenericJsonPlugin().Open(MemorySaveSet.SingleFile("ue4savegame.dpx.sav", Encoding.UTF8.GetBytes(source), "PPSA02343"));
            var tabs = Assert.IsType<TabsNode>(document.BuildView());
            return Assert.Single(Assert.Single(tabs.Tabs.SelectMany(t => t.Children).OfType<MapNode>()).Markers).Id;
        }
    }

    private static void AssertTarget(ISaveDocument document, TabsNode root, ViewTarget target)
    {
        IReadOnlyList<ViewNode> current = [root];
        foreach (var part in target.Path)
        {
            if (part.StartsWith("tab:", StringComparison.Ordinal))
                current = root.Tabs.Single(t => t.Title == part[4..]).Children;
            else
            {
                var section = current.OfType<SectionNode>().Single(s => s.Title == part[8..] || s.LazyId == part[8..]);
                current = section.LazyId is { } id ? document.ExpandNode(id) : section.Children;
            }
        }
        Assert.Contains(current.OfType<FieldNode>(), f => f.ValueId == target.Id);
        _ = document.GetValue(target.Id);
    }
}
