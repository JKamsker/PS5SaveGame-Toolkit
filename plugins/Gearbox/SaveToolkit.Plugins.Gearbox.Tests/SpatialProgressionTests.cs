using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SpatialProgressionTests
{
    [Theory]
    [InlineData("PPSA01463", 315, "siren/brawl/root-to-rise", "Root to Rise")]
    [InlineData("PPSA01493", 110, "spellshot/spell-sniper", "Spell Sniper")]
    public void SkillPacks_AreAttributedAndCalculatorFactsNeverInventSaveJoins(string title, int count, string key, string name)
    {
        var plugin = new GearboxPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var references = store.Pin(plugin, title);
        var catalog = references.Catalog(GearboxSkills.CatalogId)!;
        Assert.Equal(count, catalog.Count);
        var record = catalog.Find(key)!;
        Assert.Equal(name, record.Name);
        Assert.Equal("Rights reserved by Gearbox Software / 2K; used with attribution per owner decision 2026-10-29", record.Dataset.Attribution.License);
        Assert.Contains("no save-subobject", record.Attributes["join"]);
        Assert.NotEmpty(record.Description!);
        Assert.Equal("5", record.Attributes["maxPoints"]);
        var file = GearboxTestData.PrimaryCharacter(title);
        using var document = (GearboxDocument)plugin.Open(file, references);
        var page = document.Query(GearboxSkills.CatalogId, new() { Text = name, Limit = 1 });
        var field = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["name"]);
        Assert.Contains("Rights reserved by Gearbox Software / 2K", field.Description);
        Assert.True(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void SampleGraphsAndMaps_AreBoundedHaveTextAndReachableTargetsAndPreserveBytes(string title)
    {
        var plugin = new GearboxPlugin();
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => plugin.Probe(f).IsMatch && !f.Directory.IsBackup))
        {
            using var document = plugin.Open(file);
            var nodes = GearboxGroupTests.Walk(document, document.BuildView()).ToArray();
            var graphs = nodes.OfType<GraphNode>().ToArray();
            Assert.NotEmpty(graphs);
            foreach (var graph in graphs)
            {
                SpatialViewValidation.Validate(graph);
                Assert.NotEmpty(graph.TextAlternative);
                Assert.All(graph.Items, item =>
                {
                    Assert.NotNull(item.Target);
                    Assert.Contains(nodes.OfType<FieldNode>(), field => field.ValueId == item.Target.Id);
                    Assert.Equal(ViewTargetKind.Field, item.Target.Kind);
                    Assert.Contains(item.Target.Path, part => part.StartsWith("section:skills/", StringComparison.Ordinal));
                });
            }
            var named = graphs.Where(g => g.Items.Any(i => i.Id.StartsWith("gate/", StringComparison.Ordinal))).ToArray();
            Assert.NotEmpty(named);
            Assert.All(named.SelectMany(g => g.Items), item => Assert.Equal(GraphState.Unknown, item.State));
            Assert.All(named.SelectMany(g => g.Edges), edge => Assert.StartsWith("gate/", edge.From));
            var maps = nodes.OfType<MapNode>().ToArray();
            if (file.FileName == "1") Assert.NotEmpty(maps);
            foreach (var map in maps)
            {
                SpatialViewValidation.Validate(map);
                Assert.Null(map.Background);
                Assert.NotNull(map.ExternalSource);
                Assert.All(map.Markers, marker =>
                {
                    Assert.Null(marker.X);
                    Assert.Null(marker.Y);
                    Assert.NotNull(marker.Target);
                    Assert.Contains(nodes.OfType<FieldNode>(), field => field.ValueId == marker.Target.Id);
                });
            }
            Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        }
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void GroupSpatialTargets_AreNamespacedAndResolveToTheOwningMember(string title)
    {
        var plugin = new GearboxPlugin();
        var file = GearboxTestData.PrimaryCharacter(title);
        var proposal = Assert.Single(plugin.ProposeGroups(file));
        using var group = plugin.OpenGroup(GearboxGroupTests.Input(file.Directory.SaveSet, proposal), IReferenceContext.Empty);
        var nodes = GearboxGroupTests.Walk(group, group.BuildView()).ToArray();
        foreach (var graph in nodes.OfType<GraphNode>())
        {
            SpatialViewValidation.Validate(graph);
            foreach (var item in graph.Items)
            {
                Assert.NotNull(item.Target);
                var field = Assert.Single(nodes.OfType<FieldNode>(), f => f.ValueId == item.Target.Id);
                Assert.Equal(field.Value, group.GetValue(item.Target.Id));
                Assert.Equal("section:Gearbox character + profile", item.Target.Path[0]);
                Assert.Contains(item.Target.Path, part => part.StartsWith("section:character/skills/", StringComparison.Ordinal)
                    || part.StartsWith("section:profile/skills/", StringComparison.Ordinal));
            }
        }
        Assert.All(nodes.OfType<MapNode>(), map =>
        {
            SpatialViewValidation.Validate(map);
            Assert.All(map.Markers, marker => Assert.StartsWith("character/", marker.Target!.Id));
        });
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void LargeProgressionCollections_SearchFilterSortBeyondTheFirstPage(string title)
    {
        var plugin = new GearboxPlugin();
        var file = GearboxTestData.PrimaryCharacter(title);
        var container = GearboxContainer.Parse(file.ReadAllBytes(), title, false);
        var root = container.Decode();
        var ability = root.Child("ability_data")!;
        foreach (var field in ability.All("tree_item_list").ToArray()) ability.Remove(field);
        var playthrough = root.Children("mission_playthroughs_data").First();
        foreach (var field in playthrough.All("mission_list").ToArray()) playthrough.Remove(field);
        for (var index = 0; index < 1200; index++)
        {
            Add(ability, "tree_item_list", message =>
            {
                message.SetText("item_asset_path", "/Game/Test/Skill" + index);
                message.SetInteger("points", index % 6);
                message.SetInteger("max_points", 5);
                message.SetInteger("tree_identifier", index % 3);
            });
            Add(playthrough, "mission_list", message =>
            {
                message.SetText("mission_class_path", "/Game/Test/Mission" + index);
                message.SetInteger("status", index % 4);
            });
        }
        using var document = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(file, container.Encode(root)));
        _ = document.BuildView();
        var skills = document.Query(GearboxSkills.AllocationsId, new() { Text = "/Game/Test/Skill1199" });
        Assert.Equal(1200, skills.TotalCount);
        Assert.Single(skills.Rows);
        Assert.Equal(1, skills.FilteredCount);
        var filtered = document.Query(GearboxSkills.AllocationsId, new() { Filters = new Dictionary<string, string> { ["points"] = "5" }, Limit = 500 });
        Assert.Equal(200, filtered.FilteredCount);
        Assert.Equal(200, filtered.Rows.Count);
        var missions = document.Query(GearboxMissions.CollectionId, new() { Text = "/Game/Test/Mission1199" });
        Assert.Single(missions.Rows);
        Assert.Equal("Failed", Assert.IsType<TextNode>(missions.Rows[0].Cells["status"]).Text);
        Assert.Equal(missions.Rows[0].Key, document.Query(GearboxMissions.CollectionId, new() { Text = "/Game/Test/Mission1199" }).Rows[0].Key);
        Assert.Throws<ArgumentException>(() => document.Query(GearboxSkills.CatalogId, new() { SortColumn = "invalid" }));
        var skillPages = document.ExpandNode("skills/stored").OfType<SectionNode>().ToArray();
        Assert.Equal(12, skillPages.Length);
        foreach (var section in skillPages)
        {
            var children = document.ExpandNode(section.LazyId!);
            Assert.True(children.Count <= 101);
            SpatialViewValidation.Validate(Assert.Single(children.OfType<GraphNode>()));
        }
        foreach (var section in document.ExpandNode("missions/checklist").OfType<SectionNode>())
        {
            var children = document.ExpandNode(section.LazyId!);
            Assert.True(children.Count <= 101);
            SpatialViewValidation.Validate(Assert.Single(children.OfType<MapNode>()));
        }
    }

    private static void Add(ProtoMessage parent, string list, Action<ProtoMessage> populate)
    {
        var schema = parent.Schema;
        var spec = schema.Spec(parent.Type, list);
        var child = new ProtoMessage(schema, spec.Type, []);
        populate(child);
        parent.Add(new ProtoField(schema.Tag(parent.Type, list), 2, spec, [], Array.Empty<byte>(), child));
    }
}
