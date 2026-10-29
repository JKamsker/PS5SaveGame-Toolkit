using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class FieldMetadataR4Tests
{
    [Fact]
    public void ColdProgressionDiscoveryOmitsXpWhenConsequencesExceedTheWorkBudget()
    {
        using var document = EditCoverageData.Create(false, root =>
        {
            for (var i = 0; i < GearboxInventory.DirectLookupWorkLimit + 200; i++)
            {
                var stat = GearboxTestData.AddMessage(root, "game_stats_data");
                stat.SetText("stat_path", "unrelated/stat/" + i);
            }
        });
        var before = document.Serialize().Single().Content.ToArray();

        Assert.Empty(((IFieldMetadataDocument)document).ResolveFieldMetadata(["character/xp"]));
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingProgressionScalarsReportLogicalDefaultCreationForXpAndLevel(bool wonderlands)
    {
        foreach (var requested in new[] { "character/xp", "character/level" })
        {
            using var document = EditCoverageData.Create(wonderlands, root =>
            {
                GearboxTestData.AddMessage(root, "ability_data");
                var stat = GearboxTestData.AddMessage(root, "game_stats_data");
                stat.SetText("stat_path", CharacterProgression.LevelStat(wonderlands));
            });
            var metadata = ((IFieldMetadataDocument)document).ResolveFieldMetadata([requested]);
            Assert.Equal([requested], metadata.Select(field => field.ValueId));
            var before = document.Serialize().Single().Content.ToArray();
            var targetLevel = Math.Min(30, document.Root.Schema.Experience.Count);
            var target = document.Root.Schema.Experience[targetLevel - 1];
            var outcome = document.ApplyEdits([new(requested,
                requested == "character/xp" ? FieldValue.Of(target) : FieldValue.Of(targetLevel))]);

            Assert.True(outcome.Succeeded);
            Assert.Contains(outcome.Changes, change => change.ValueId == "character/recorded-level");
            Assert.Contains(outcome.Changes, change => change.ValueId == (wonderlands ? "character/skill-points" : "character/tree-grade"));
            var changed = document.Serialize().Single().Content.ToArray();
            Assert.NotEqual(before, changed);
            var redo = document.RestoreMemento(outcome.Revert!);
            Assert.Equal(before, document.Serialize().Single().Content.ToArray());
            _ = document.RestoreMemento(redo);
            Assert.Equal(changed, document.Serialize().Single().Content.ToArray());
        }
    }

    [Fact]
    public void RawFlagsAreReadonlyAndQualifiedProjectionUsesExactCanonicalSpec()
    {
        using var document = EditCoverageData.Create(false, root =>
        {
            root.All(GearboxInventory.Backpack).First().Message!.SetInteger("flags", 0x80000001);
        });
        var metadata = ((IFieldMetadataDocument)document).ResolveFieldMetadata([
            "inventory_items/item-0/flags", "item-0/flags", "item-0/mark"]);
        var raw = Assert.Single(metadata, field => field.ValueId == "item-0/flags");

        Assert.Equal(FieldBindingKind.Derived, raw.BindingKind);
        Assert.True(raw.Spec.ReadOnly);
        Assert.DoesNotContain(metadata, field => field.ValueId == "inventory_items/item-0/flags");
        var before = document.Serialize().Single().Content.ToArray();
        Assert.False(document.ApplyEdits([new("item-0/flags", FieldValue.Of(0))]).Succeeded);
        Assert.False(document.ApplyEdits([new("inventory_items/item-0/flags", FieldValue.Of(0))]).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());

        var mark = document.ApplyEdits([new("item-0/mark", FieldValue.Of(1))]);
        Assert.True(mark.Succeeded);
        Assert.Equal(0x80000003, document.Inventory.Find("item-0").Field.Message!.Integer("flags"));
    }

    [Fact]
    public void GroupCandidateMetadataResolutionChunksMoreThanTwoHundredEdits()
    {
        using var character = EditCoverageData.Create(false, root =>
        {
            for (var i = 0; i < 201; i++) EditCoverageData.AddPool(root, EditCoverageData.OtherAmmoPath, i);
        });
        using var profile = EditCoverageData.Create(false, profile: true);
        var set = new MemorySaveSet().With("PPSA01463", "R4", new Dictionary<string, byte[]>
        {
            ["1"] = character.Serialize().Single().Content.ToArray(),
            ["profile"] = profile.Serialize().Single().Content.ToArray(),
        });
        using var group = (GearboxGroupDocument)new GearboxPlugin().OpenGroup(new(((ISaveSet)set).AllFiles.Select(file =>
            new DocumentGroupFile(file.FileName == "profile" ? "profile" : "character", file)).ToArray()), IReferenceContext.Empty);
        var before = group.Serialize().Select(output => output.Content.ToArray()).OrderBy(bytes => Convert.ToHexString(bytes)).ToArray();
        var edits = Enumerable.Range(0, 201).Select(index =>
            new ValueEdit($"character/ammo/{index}", FieldValue.Of(1.0))).ToArray();

        var outcome = group.ApplyEdits(edits);

        Assert.True(outcome.Succeeded);
        var changed = group.Serialize().Select(output => output.Content.ToArray()).OrderBy(bytes => Convert.ToHexString(bytes)).ToArray();
        Assert.Equal(2, changed.Length);
        Assert.NotEqual(before, changed);
        var redo = group.RestoreMemento(outcome.Revert!);
        Assert.Equal(before, group.Serialize().Select(output => output.Content.ToArray()).OrderBy(bytes => Convert.ToHexString(bytes)).ToArray());
        _ = group.RestoreMemento(redo);
        Assert.Equal(changed, group.Serialize().Select(output => output.Content.ToArray()).OrderBy(bytes => Convert.ToHexString(bytes)).ToArray());
    }

    [Fact]
    public void EstablishedLateItemCurrencyAndWorldBindingsSurviveCloneAndReopen()
    {
        using var document = EditCoverageData.Create(false, root =>
        {
            for (var i = 0; i < 4_997; i++) EditCoverageData.AddItem(root, 1);
            for (var i = 0; i < GearboxInventory.DirectLookupWorkLimit + 100; i++)
                root.Add(new ProtoField(1900, 0, null, [], 0UL, null));
            var category = GearboxTestData.AddMessage(root, "inventory_category_list");
            category.SetInteger("base_category_definition_hash", 618814354);
            category.SetInteger("quantity", 100);
            var world = GearboxTestData.AddMessage(root, "game_state_save_data_for_playthrough");
            world.SetInteger("mayhem_level", 3);
        });
        _ = document.Inventory.Entries.ToArray();
        _ = document.BuildView();
        _ = document.ExpandNode("difficulty");
        _ = ((IFieldMetadataDocument)document).ResolveFieldMetadata(["item-4999/level", "currency/618814354", "world/0/difficulty"]);
        var before = document.Serialize().Single().Content.ToArray();
        var outcome = document.ApplyEdits([
            new("item-4999/level", FieldValue.Of(document.GetValue("item-4999/level").AsInt32() + 1)),
            new("currency/618814354", FieldValue.Of(200)),
            new("world/0/difficulty", FieldValue.Of(4)),
        ]);

        Assert.True(outcome.Succeeded);
        var changed = document.Serialize().Single().Content.ToArray();
        Assert.NotEqual(before, changed);
        var redo = document.RestoreMemento(outcome.Revert!);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(changed, document.Serialize().Single().Content.ToArray());
        var reopenedContainer = GearboxContainer.Parse(changed, "PPSA01463", false);
        using var reopened = new GearboxDocument("1", "PPSA01463", reopenedContainer, reopenedContainer.Decode(), IReferenceContext.Empty);
        _ = reopened.Inventory.Entries.ToArray();
        _ = reopened.BuildView();
        _ = reopened.ExpandNode("difficulty");
        Assert.Equal(200, reopened.GetValue("currency/618814354").AsInt64());
        Assert.Equal(4, reopened.GetValue("world/0/difficulty").AsInt64());
    }

    [Fact]
    public void MythAllocationTargetedPresentationUsesTheSameReadWritePolicyForProfileAndMirror()
    {
        using var profile = EditCoverageData.Create(true, root =>
        {
            var myth = GearboxTestData.AddMessage(root, "player_prestige");
            myth.Add(new ProtoField(root.Schema.Tag(myth.Type, "points_spent_by_index_order"), 0,
                root.Schema.Spec(myth.Type, "points_spent_by_index_order"), [], 3UL, null));
        }, profile: true, coordinated: true);
        using var mirror = EditCoverageData.Create(true, root =>
        {
            var cloud = GearboxTestData.AddMessage(root, "profile_cloud_data");
            var myth = GearboxTestData.AddMessage(cloud, "player_prestige");
            myth.Add(new ProtoField(root.Schema.Tag(myth.Type, "points_spent_by_index_order"), 0,
                root.Schema.Spec(myth.Type, "points_spent_by_index_order"), [], 4UL, null));
        }, coordinated: true);

        var profileField = Assert.Single(((IFieldMetadataDocument)profile)
            .ResolveFieldMetadata(["account/myth/0/points"]));
        var mirrorField = Assert.Single(((IFieldMetadataDocument)mirror)
            .ResolveFieldMetadata(["account/myth-mirror/0/points"]));

        Assert.Equal(FieldBindingKind.Primary, profileField.BindingKind);
        Assert.Equal(FieldBindingKind.Primary, mirrorField.BindingKind);
        Assert.Equal(profileField.Spec, mirrorField.Spec);
        Assert.Equal(Confidence.Inferred, profileField.Presentation!.Confidence);
        Assert.Contains(SerialWrites.Warning, profileField.Presentation.Description, StringComparison.Ordinal);
        Assert.Equal(3, profile.GetValue(profileField.ValueId).AsInt64());
        Assert.Equal(4, mirror.GetValue(mirrorField.ValueId).AsInt64());
    }


}

