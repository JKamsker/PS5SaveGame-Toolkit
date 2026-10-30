using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class FieldMetadataR6Tests
{
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("999999")]
    public void MythLabelsCannotReplaceEmbeddedWritePolicy(string? overrideCap)
    {
        using var character = EditCoverageData.Create(true, root =>
        {
            var cloud = GearboxTestData.AddMessage(root, "profile_cloud_data");
            GearboxTestData.AddMessage(cloud, "player_prestige").SetInteger("points_spent_by_index_order", 3);
        });
        using var profile = EditCoverageData.Create(true, root =>
            GearboxTestData.AddMessage(root, "player_prestige").SetInteger("points_spent_by_index_order", 3), profile: true);
        var policy = SerialDatabase.For(true).Find(GearboxWorldCatalog.CatalogId, "myth:0")!;
        var attributes = policy.Attributes.ToDictionary();
        if (overrideCap is null) attributes.Remove("upstreamCap"); else attributes["upstreamCap"] = overrideCap;
        var context = new MetadataR6Data.Context(policy with { Name = "Translated Myth stat", Attributes = attributes });
        using var group = MetadataR6Data.Group(MetadataR6Data.Set(character, profile), context);
        var before = SelectionTestData.Bytes(group);
        foreach (var role in new[] { "profile", "character" })
        {
            var member = role == "profile" ? group.Profile : group.Character;
            var prefix = role == "profile" ? "account/myth" : "account/myth-mirror";
            var id = prefix + "/0/points";
            var targeted = Assert.Single(member.ResolveFieldMetadata([id]));
            var expectedMax = int.Parse(policy.Attributes["upstreamCap"]);
            Assert.Equal(expectedMax, Assert.IsType<IntegerSpec>(targeted.Spec).Max);
            var ownership = Assert.Single(group.ResolveFieldContext([role + "/" + id]));
            Assert.Equal(role, ownership.Role);
            var page = member.Query(prefix + "/allocations", new() { Limit = 1 });
            var rendered = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["points"]);
            Assert.Equal(targeted.Spec, rendered.Spec);
            Assert.Equal(targeted.Presentation!.Description, rendered.Description);
            Assert.Contains("Translated Myth stat", Assert.IsType<FieldNode>(page.Rows[0].Cells["name"]).Value.AsString());
            Assert.Equal(targeted.Spec, Assert.Single(member.ResolveFieldMetadata([id])).Spec);
            Assert.False(group.ApplyEdits([new(role + "/" + id, FieldValue.Of(expectedMax + 1))]).Succeeded);
        }
        SelectionTestData.AssertBytes(group, before);
    }

    [Fact]
    public void NumericStorageCannotGrantExtraHeroOrMythSetters()
    {
        using var character = EditCoverageData.Create(true, root =>
        {
            var hero = GearboxTestData.AddMessage(root, "hero_points_save_data");
            hero.SetInteger("strength", 7);
            hero.SetInteger("respec_count", 3);
        });
        using var profile = EditCoverageData.Create(true, root =>
        {
            var myth = GearboxTestData.AddMessage(root, "player_prestige");
            myth.SetInteger("prestige_experience", 10);
            myth.SetInteger("has_seen_tutorial", 0);
        }, profile: true);
        using var group = MetadataR6Data.Group(MetadataR6Data.Set(character, profile));
        var before = SelectionTestData.Bytes(group);
        var ids = new[] { "character/account/hero/respec_count", "profile/account/myth/prestige_experience",
            "profile/account/myth/has_seen_tutorial", "character/account/hero/strength" };
        Assert.Equal([ids[1], ids[3]], group.ResolveFieldMetadata(ids).Select(field => field.ValueId));
        Assert.Equal([ids[1], ids[3]], group.ResolveFieldContext(ids).Select(field => field.ValueId));
        Assert.Empty(group.Character.ResolveFieldMetadata(["account/hero/respec_count"]));
        Assert.Empty(group.Profile.ResolveFieldMetadata(["account/myth/has_seen_tutorial"]));
        Assert.False(group.ApplyEdits([new(ids[0], FieldValue.Of(4))]).Succeeded);
        Assert.False(group.ApplyEdits([new(ids[2], FieldValue.Of(2))]).Succeeded);
        SelectionTestData.AssertBytes(group, before);
        Assert.True(group.ApplyEdits([new(ids[1], FieldValue.Of(11)), new(ids[3], FieldValue.Of(8))]).Succeeded);
    }

    [Fact]
    public void CountsPublishOnlyOwnedContainersIncludingEmptyLists()
    {
        using var group = GroupProtocolTests.Open(false);
        var before = SelectionTestData.Bytes(group);
        var identity = group.Profile.Inventory.Capture();
        var ids = new[] { "character/bank_inventory_list/count", "profile/bank_inventory_list/count",
            "profile/inventory_items/count", "character/inventory_items/count", "profile/lost_loot_inventory_list/count" };
        var expected = new[] { ids[1], ids[3], ids[4] };
        Assert.Equal(expected, group.ResolveFieldMetadata(ids).Select(field => field.ValueId));
        var contexts = group.ResolveFieldContext(ids);
        Assert.Equal(expected, contexts.Select(field => field.ValueId));
        Assert.All(contexts, field => Assert.Null(field.Provenance));
        Assert.Equal(0, group.GetValue(ids[1]).AsInt64());
        Assert.Equal(0, group.GetValue(ids[4]).AsInt64());
        Assert.Equal(identity.NextId, group.Profile.Inventory.Capture().NextId);
        Assert.Equal(identity.Initialized, group.Profile.Inventory.Capture().Initialized);
        SelectionTestData.AssertBytes(group, before);
    }

    [Fact]
    public void NestedMythColdWorkChargesUnrelatedFieldsAndOmitsUnreachedValues()
    {
        using var character = EditCoverageData.Create(true);
        using var profile = EditCoverageData.Create(true, root =>
        {
            var myth = GearboxTestData.AddMessage(root, "player_prestige");
            for (var i = 0; i < 5000; i++) myth.Add(new ProtoField(1900, 0, null, [], 0UL, null));
            myth.SetInteger("points_spent_by_index_order", 3);
        }, profile: true);
        using var group = MetadataR6Data.Group(MetadataR6Data.Set(character, profile));
        var before = SelectionTestData.Bytes(group);
        Assert.False(GearboxAccountViews.TryIntegerAt(group.Profile.Root.Child("player_prestige")!,
            "points_spent_by_index_order", 0, out _, out var visits));
        Assert.Equal(GearboxInventory.DirectLookupWorkLimit + 1, visits);
        Assert.Empty(group.ResolveFieldContext(["profile/account/myth/0/points"]));
        Assert.Empty(group.ResolveFieldMetadata(["profile/account/myth/0/points"]));
        SelectionTestData.AssertBytes(group, before);
    }

    [Fact]
    public void EstablishedLateItemUsesOneCurrentFieldAfterCloneAndGroupRestore()
    {
        using var character = EditCoverageData.Create(false, root =>
        {
            for (var i = 0; i < 4997; i++) EditCoverageData.AddItem(root, 1);
        });
        using var profile = EditCoverageData.Create(false, profile: true);
        var set = MetadataR6Data.Set(character, profile);
        using var group = MetadataR6Data.Group(set);
        _ = group.Character.Inventory.Entries.ToArray();
        var snapshot = group.CaptureMemento();
        _ = group.RestoreMemento(snapshot);
        var visits = group.Character.Inventory.DirectFieldsVisited;
        Assert.Single(group.ResolveFieldContext(["character/item-4999/level"]));
        Assert.Equal(1, group.Character.Inventory.DirectFieldsVisited - visits);
        var before = SelectionTestData.Bytes(group);
        var oldLevel = group.GetValue("character/item-4999/level").AsInt32();
        var outcome = group.ApplyEdits([new("character/item-4999/level", FieldValue.Of(oldLevel + 1))]);
        Assert.True(outcome.Succeeded);
        visits = group.Character.Inventory.DirectFieldsVisited;
        Assert.Single(group.ResolveFieldContext(["character/item-4999/level"]));
        Assert.Equal(1, group.Character.Inventory.DirectFieldsVisited - visits);
        var changed = SelectionTestData.Bytes(group);
        var path = changed.Keys.Single(key => key.EndsWith("/1", StringComparison.Ordinal));
        var reopened = GearboxContainer.Parse(changed[path], "PPSA01463", false).Decode();
        var serial = InventoryEntry.SerialOf(reopened.All(GearboxInventory.Backpack).ElementAt(4999));
        _ = IndependentSerialProbe.Decode(serial);
        Assert.Equal(oldLevel + 1, new SerialReader(false).Read(serial).Level);
        var redo = group.RestoreMemento(outcome.Revert!);
        SelectionTestData.AssertBytes(group, before);
        _ = group.RestoreMemento(redo);
        SelectionTestData.AssertBytes(group, changed);
    }

    [Theory]
    [InlineData(false, "xp-first")]
    [InlineData(false, "explicit-first")]
    [InlineData(false, "render-first")]
    [InlineData(true, "xp-first")]
    [InlineData(true, "explicit-first")]
    [InlineData(true, "render-first")]
    public void RecordedStatisticsHaveOneStablePrimaryPerRecord(bool wonderlands, string order)
    {
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            for (var i = 0; i < 2; i++)
            {
                var stat = GearboxTestData.AddMessage(root, "game_stats_data");
                stat.SetText("stat_path", CharacterProgression.LevelStat(wonderlands));
                stat.SetInteger("stat_value", 20);
            }
        });
        if (order == "render-first") _ = document.BuildView();
        else _ = document.ResolveFieldMetadata([order == "xp-first" ? "character/xp" : "character/recorded-level"]);
        var metadata = document.ResolveFieldMetadata(["character/recorded-level", "character/recorded-level/0",
            "character/recorded-level/1", "character/xp"]);
        Assert.Equal(["character/recorded-level", "character/recorded-level/1", "character/xp"], metadata.Select(field => field.ValueId));
        Assert.All(metadata, field => Assert.Equal(field.ValueId, field.CanonicalValueId));
        var before = document.Serialize().Single().Content.ToArray();
        var outcome = document.ApplyEdits([new("character/xp", FieldValue.Of(document.Root.Schema.Experience[20]))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(["character/recorded-level", "character/recorded-level/1"], outcome.Changes
            .Where(change => change.ValueId.StartsWith("character/recorded-level", StringComparison.Ordinal)).Select(change => change.ValueId));
        var changed = document.Serialize().Single().Content.ToArray();
        var redo = document.RestoreMemento(outcome.Revert!);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(changed, document.Serialize().Single().Content.ToArray());
        Assert.Equal(2, document.ResolveFieldMetadata(["character/recorded-level", "character/recorded-level/1"]).Count);
    }
}
