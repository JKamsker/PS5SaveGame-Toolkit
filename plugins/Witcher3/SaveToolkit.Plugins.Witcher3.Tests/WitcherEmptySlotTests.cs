using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherEmptySlotTests
{
    [Fact]
    public void EverySampleHidesOnlyConfirmedAbilitySlotsAndKeepsAllNativeRecords()
    {
        var files = EnabledWriteFixture.Files();
        Assert.Equal(8, files.Count);
        var plugin = new Witcher3Plugin();
        foreach (var file in files)
        {
            var before = EnabledWriteFixture.Read(file.ReadAllBytes());
            using var document = plugin.Open(file);
            using var session = new DocumentSession("empty-slots", file, plugin, document);
            var root = Assert.IsType<TabsNode>(session.BuildView());
            var nodes = root.Tabs.Single(t => t.Title == "Abilities and Gwent").Children;
            Assert.DoesNotContain(nodes.OfType<SectionNode>(), s => s.Title.StartsWith("Slots ", StringComparison.Ordinal));
            Assert.DoesNotContain(nodes.OfType<SectionNode>(), s => s.LazyId?.StartsWith("gwent/", StringComparison.Ordinal) == true);
            foreach (var array in before.Features.Abilities)
            {
                var id = AbilityCollections.Prefix + array.Name;
                var table = Assert.Single(nodes.OfType<QueryableTableNode>(), t => t.CollectionId == id);
                var metadata = Assert.Single(((IQueryableDocument)document).Collections, c => c.Id == id);
                Assert.Equal(metadata.EmptyRows, table.EmptyRows);
                Assert.True(table.EmptyRows!.HideByDefault);
                Assert.Equal("name", table.EmptyRows.LabelColumnKey);
                var emptyCount = array.Name switch { "skills" => 6, "skillSlots" => 16, "mutagenSlots" => 4, _ => 0 };
                Assert.Equal(emptyCount, array.Entries.Count(e => e.IsEmpty));
                var hidden = session.QueryCollection(id, new() { Limit = 200 });
                Assert.Equal(array.Entries.Count, hidden.TotalCount);
                Assert.Equal(array.Entries.Count - emptyCount, hidden.FilteredCount);
                Assert.Equal(emptyCount, hidden.HiddenEmptyCount);
                Assert.All(hidden.Rows, row => Assert.False(row.IsEmpty));
                var direct = ((IQueryableDocument)document).Query(id, new() { Limit = 200 });
                Assert.Equal(hidden.Rows.Select(r => r.Key), direct.Rows.Select(r => r.Key));
                Assert.Equal(hidden.HiddenEmptyCount, direct.HiddenEmptyCount);
                var shown = session.QueryCollection(id, new() { Limit = 200, IncludeEmpty = true });
                Assert.Equal(array.Entries.Count, shown.FilteredCount);
                Assert.Equal(0, shown.HiddenEmptyCount);
                Assert.Equal(array.Entries.Select(e => e.Id).Order(), shown.Rows.Select(r => r.Key).Order());
                Assert.All(shown.Rows.Where(r => r.IsEmpty), row =>
                    Assert.Equal("Empty slot", Assert.IsType<TextNode>(row.Cells["name"]).Text));
                Assert.All(session.Expand($"ability/{array.Name}/0"), node => Assert.IsType<SectionNode>(node));
            }

            Assert.Single(nodes.OfType<QueryableTableNode>(), t => t.CollectionId == GwentCollection.CollectionId);
            var inventory = session.QueryCollection(InventoryViews.CollectionId, new() { Limit = 200 });
            Assert.Equal(before.Player.Inventory!.Items.Count, inventory.Rows.Count);
            Assert.All(inventory.Rows, row => Assert.False(row.IsEmpty));
            Assert.Equal(0, inventory.HiddenEmptyCount);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData(null, 95)]
    [InlineData(false, 95)]
    [InlineData(true, 101)]
    public void SelectAllMatchingResolvesVisibilityAndExclusionsBeforePlanning(bool? includeEmpty, int count)
    {
        var file = EnabledWriteFixture.Files()[0];
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("slot-scope", file, plugin, document);
        _ = session.BuildView();
        var scope = new ActionScope { QueryScope = new(AbilityCollections.Prefix + "skills",
            new() { IncludeEmpty = includeEmpty, Limit = 1 }, session.State.Revision) { ExcludedRowKeys = ["S_Sword_1"] } };
        var preview = session.PlanAction(RepairPlanning.ActionId, new Dictionary<string, JsonElement>(), scope);
        Assert.Equal(count, preview.Scope.RowKeys!.Count);
        Assert.Equal(includeEmpty == true, preview.Scope.QueryScope!.Query.IncludeEmpty);
        Assert.DoesNotContain("S_Sword_1", preview.Scope.RowKeys);
        Assert.Equal(includeEmpty == true, preview.Scope.RowKeys.Contains("unnamed-slot-0"));
        // Inventory repair must still refuse a selection from the ability collection.
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Null(preview.PlanId);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EmptyFilteringPrecedesPagingSearchFiltersAndSortAcrossTheCompleteModel()
    {
        var entries = Enumerable.Range(0, 550).Select(i => new AbilityEntry($"slot-{i:D3}", new TaggedRecord([])) {
            IsEmpty = i % 2 == 0,
        }).ToArray();
        var view = new AbilityCollections([new("skills", entries)], null, null,
            (_, e) => new TableRow(e.Id, new Dictionary<string, ViewNode>()) { IsEmpty = e.IsEmpty });
        var id = AbilityCollections.Prefix + "skills";
        var query = new QueryableCollectionQuery { Text = "slot-5", SortColumn = "id", Descending = true, Offset = 1, Limit = 2 };
        var page = view.Query(id, query);
        Assert.Equal((550, 25, 25), (page.TotalCount, page.FilteredCount, page.HiddenEmptyCount));
        Assert.Equal(["slot-547", "slot-545"], page.Rows.Select(r => r.Key));
        var shown = view.Query(id, query with { IncludeEmpty = true });
        Assert.Equal((50, 0), (shown.FilteredCount, shown.HiddenEmptyCount));
        Assert.Equal(["slot-548", "slot-547"], shown.Rows.Select(r => r.Key));
        var exact = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["id"] = "slot-548" } };
        var hidden = view.Query(id, exact);
        Assert.Empty(hidden.Rows);
        Assert.Equal((0, 1), (hidden.FilteredCount, hidden.HiddenEmptyCount));
        Assert.Equal("slot-548", Assert.Single(view.Query(id, exact with { IncludeEmpty = true }).Rows).Key);
        Assert.Equal(0, view.Query(id, exact with { Text = "absent" }).HiddenEmptyCount);
        Assert.Equal(25, view.Query(id, query with { Offset = int.MaxValue }).HiddenEmptyCount);
    }

    [Fact]
    public void EmptyRulesPreserveUnknownLayoutsAndNamedZeroRankDefinitions()
    {
        TaggedProperty Property(string name, string type, params byte[] value) => new(name, type, 0, new byte[8].Concat(value).ToArray());
        var identity = Property("id", "Int32", 1, 0, 0, 0);
        var skill = Property("socketedSkill", "ESkill", 1, 0);
        string[] names = ["S_SUndefined", "S_UnknownModdedSkill"];
        Assert.True(AbilitySlots.IsEmpty("skills", new([]), names));
        Assert.False(AbilitySlots.IsEmpty("skills", new([Property("level", "Int32", 0, 0, 0, 0)]), names));
        Assert.True(AbilitySlots.IsEmpty("skillSlots", new([identity]), names));
        Assert.True(AbilitySlots.IsEmpty("skillSlots", new([identity, skill]), names));
        Assert.False(AbilitySlots.IsEmpty("skillSlots", new([identity, Property("socketedSkill", "ESkill", 2, 0)]), names));
        Assert.False(AbilitySlots.IsEmpty("skillSlots", new([identity, Property("futureOccupant", "Unknown", 0)]), names));
        Assert.False(AbilitySlots.IsEmpty("skillSlots", new([identity, Property("socketedSkill", "Unknown", 0)]), names));
        Assert.False(AbilitySlots.IsEmpty("skillSlots", new([identity, Property("groupID", "Unknown", 0)]), names));
        Assert.False(AbilitySlots.IsEmpty("skillSlots", new([identity, Property("unlocked", "Bool", 2)]), names));
        var equipment = Property("equipmentSlot", "EEquipmentSlots", 2, 0);
        Assert.True(AbilitySlots.IsEmpty("mutagenSlots", new([equipment]), names));
        Assert.False(AbilitySlots.IsEmpty("mutagenSlots", new([equipment, Property("item", "SItemUniqueId", 0, 0, 0)]), names));
        Assert.False(AbilitySlots.IsEmpty("mutagenSlots", new([equipment, Property("future", "Unknown", 0)]), names));
        Assert.False(AbilitySlots.IsEmpty("mutagenSlots", new([equipment, Property("skillGroupID", "Unknown", 0)]), names));
        Assert.False(AbilitySlots.IsEmpty("mutations", new([]), names));
    }

    [Fact]
    public void EditingAVisibleQueriedSkillPreservesItsNativeSlotAndEveryOtherByte()
    {
        var file = EnabledWriteFixture.Files()[0];
        var input = file.ReadAllBytes();
        var before = EnabledWriteFixture.Read(input);
        var rank = before.Features.SkillEdits!.Ranks.First(r => r.Original == 0 && r.Maximum > 1);
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("visible-skill", file, plugin, document);
        _ = session.BuildView();
        var row = Assert.Single(session.QueryCollection(AbilityCollections.Prefix + "skills", new() {
            Filters = new Dictionary<string, string> { ["id"] = rank.Entry.Id },
        }).Rows);
        Assert.False(row.IsEmpty);
        var field = Assert.IsType<FieldNode>(row.Cells["rank"]);
        Assert.Equal(rank.ValueId, field.ValueId);
        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming(field.ValueId, FieldValue.Of(1))]).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        EnabledWriteFixture.SkillPreservation(input, edited, rank);
        Assert.Equal(1, EnabledWriteFixture.Read(edited).Features.SkillEdits!.Find(rank.Entry.Id)!.Value);
        Assert.True(session.Undo().Ok);
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
