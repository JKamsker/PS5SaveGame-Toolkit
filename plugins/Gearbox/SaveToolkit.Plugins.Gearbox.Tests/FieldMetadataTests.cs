using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class FieldMetadataTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresenceIsReadonlyActionObservationAndStructuralUndoRetainsBytes(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands);
        var before = document.Serialize().Single().Content.ToArray();
        var presence = Assert.Single(((IFieldMetadataDocument)document).ResolveFieldMetadata(["item-0/presence"]));

        Assert.Equal(FieldBindingKind.Derived, presence.BindingKind);
        Assert.Equal("item-0/presence", presence.CanonicalValueId);
        Assert.True(presence.Spec.ReadOnly);
        var rejected = document.ApplyEdits([new("item-0/presence", FieldValue.Of(0))]);
        Assert.False(rejected.Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());

        var plan = document.PlanAction("items/duplicate", new Dictionary<string, FieldValue>(), new(["item-0"]));
        Assert.Empty(plan.Errors);
        Assert.True(Assert.Single(plan.Changes).Derived);
        var applied = document.ApplyActionPlan("items/duplicate", new Dictionary<string, FieldValue>(), new(["item-0"]), plan);
        Assert.True(applied.Succeeded);
        Assert.NotEqual(before, document.Serialize().Single().Content.ToArray());
        _ = document.RestoreMemento(applied.Revert!);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExperienceMetadataReportsEveryLiveScalarConsequenceAndUndoRestoresExactBytes(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            var ability = GearboxTestData.AddMessage(root, "ability_data");
            ability.SetInteger("ability_points", 2);
            if (!wonderlands)
                ability.Add(new ProtoField(root.Schema.Tag(ability.Type, "tree_grade"), 0,
                    root.Schema.Spec(ability.Type, "tree_grade"), [0x30, 0], 0UL, null));
            var statistic = GearboxTestData.AddMessage(root, "game_stats_data");
            statistic.SetText("stat_path", CharacterProgression.LevelStat(wonderlands));
            statistic.SetInteger("stat_value", 20);
        });
        var before = document.Serialize().Single().Content.ToArray();
        var requested = ((IFieldMetadataDocument)document).ResolveFieldMetadata(["character/xp"]);
        Assert.Equal(["character/xp"], requested.Select(field => field.ValueId));
        Assert.Equal(0, document.Root.Child("ability_data")!.Integer("tree_grade"));

        var next = document.Root.Schema.Experience[20];
        var outcome = document.ApplyEdits([new("character/xp", FieldValue.Of(next))]);
        Assert.True(outcome.Succeeded);
        if (!wonderlands) Assert.Equal(1, document.Root.Child("ability_data")!.Integer("tree_grade"));
        if (!wonderlands) Assert.Equal(1, document.GetValue("character/tree-grade").AsInt64());
        Assert.Contains(outcome.Changes, change => change.ValueId == "character/level");
        Assert.Contains(outcome.Changes, change => change.ValueId == "character/recorded-level");
        Assert.Contains(outcome.Changes, change => change.ValueId == (wonderlands ? "character/skill-points" : "character/tree-grade"));
        var changed = document.Serialize().Single().Content.ToArray();
        Assert.NotEqual(before, changed);

        var redo = document.RestoreMemento(outcome.Revert!);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(changed, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void CoordinatedAccountMetadataKeepsSeedReadonlyAndPartitionsRoles()
    {
        using var character = EditCoverageData.Create(false, root =>
        {
            var legacy = GearboxTestData.AddMessage(root, "guardian_rank");
            legacy.SetInteger("guardian_rank", 4);
            var mirror = GearboxTestData.AddMessage(root, "guardian_rank_character_data");
            mirror.SetInteger("guardian_rank", 5);
            mirror.SetInteger("guardian_reward_random_seed", 10);
        }, coordinated: true);
        var ids = new[] { "account/profile/guardian_rank", "account/legacy/guardian_rank", "account/character/guardian_rank",
            "account/character/guardian_reward_random_seed", "account/foreign/guardian_rank" };
        var metadata = ((IFieldMetadataDocument)character).ResolveFieldMetadata(ids);

        Assert.Equal(["account/legacy/guardian_rank", "account/character/guardian_rank", "account/character/guardian_reward_random_seed"],
            metadata.Select(field => field.ValueId));
        Assert.True(metadata.Single(field => field.ValueId.EndsWith("random_seed", StringComparison.Ordinal)).Spec.ReadOnly);
        Assert.All(metadata, field => Assert.Equal(Confidence.Inferred, field.Presentation!.Confidence));
        var before = character.Serialize().Single().Content.ToArray();
        Assert.False(character.ApplyEdits([new("account/character/guardian_reward_random_seed", FieldValue.Of(3))]).Succeeded);
        Assert.Equal(before, character.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void WonderlandsAccountMetadataUsesCharacterHeroMirrorAndProfileMythRoles()
    {
        using var character = EditCoverageData.Create(true, root =>
        {
            var hero = GearboxTestData.AddMessage(root, "hero_points_save_data");
            hero.SetInteger("strength", 7);
            var cloud = GearboxTestData.AddMessage(root, "profile_cloud_data");
            var mirror = GearboxTestData.AddMessage(cloud, "player_prestige");
            mirror.SetInteger("prestige_experience", 12);
        }, coordinated: true);
        var characterMetadata = ((IFieldMetadataDocument)character).ResolveFieldMetadata([
            "account/myth/prestige_experience", "account/hero/strength", "account/myth-mirror/prestige_experience", "account/profile/strength"]);
        Assert.Equal(["account/hero/strength", "account/myth-mirror/prestige_experience"], characterMetadata.Select(field => field.ValueId));

        using var profile = EditCoverageData.Create(true, root =>
        {
            var myth = GearboxTestData.AddMessage(root, "player_prestige");
            myth.SetInteger("prestige_experience", 13);
        }, profile: true, coordinated: true);
        var profileMetadata = ((IFieldMetadataDocument)profile).ResolveFieldMetadata([
            "account/myth-mirror/prestige_experience", "account/myth/prestige_experience", "account/hero/strength"]);
        Assert.Equal(["account/myth/prestige_experience"], profileMetadata.Select(field => field.ValueId));
    }

    [Fact]
    public void WorldTargetedPresentationMatchesRenderedFieldWithoutInventingUnknownPath()
    {
        using var document = EditCoverageData.Create(false, root =>
        {
            var state = GearboxTestData.AddMessage(root, "game_state_save_data_for_playthrough");
            state.SetInteger("mayhem_level", 3);
        });
        var targeted = Assert.Single(((IFieldMetadataDocument)document).ResolveFieldMetadata(["world/0/difficulty", "world/0/unlocked"]));
        var rendered = Assert.Single(document.BuildView() is TabsNode tabs
            ? tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(node => GearboxGroupTests.Walk(document, node))
                .OfType<FieldNode>().Where(field => field.ValueId == "world/0/difficulty")
            : []);
        Assert.Equal(rendered.Label, targeted.Label);
        Assert.Equal(rendered.Spec, targeted.Spec);
        Assert.Equal(rendered.Confidence, targeted.Presentation!.Confidence);
        Assert.Equal(rendered.Description, targeted.Presentation.Description);
        Assert.Equal(rendered.Warning, targeted.Presentation.Warning);
        Assert.Null(((IFieldMetadataDocument)document).ResolveFieldMetadata(["world/1/difficulty"]).SingleOrDefault()?.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetedFieldsResolveWithoutBuildingAViewAndKeepPresentation(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands);
        var original = document.Serialize().Single().Content.ToArray();
        var metadata = ((IFieldMetadataDocument)document).ResolveFieldMetadata(
            ["character/xp", "character/level", "ammo/0", "unknown/field"]);

        Assert.Equal(["character/xp", "character/level", "ammo/0"], metadata.Select(field => field.ValueId));
        Assert.All(metadata, field =>
        {
            Assert.Equal(FieldBindingKind.Primary, field.BindingKind);
            Assert.Equal(field.ValueId, field.CanonicalValueId);
            Assert.NotNull(field.Presentation);
            Assert.NotNull(field.Presentation!.Description);
        });
        Assert.Equal(document.Root.Integer("experience_points"), document.GetValue("character/xp").AsInt64());
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void MetadataRequestsAreBoundedAndMalformedRequestsDoNotMutate()
    {
        using var document = EditCoverageData.Create(false);
        var original = document.Serialize().Single().Content.ToArray();
        Assert.Throws<ArgumentOutOfRangeException>(() => ((IFieldMetadataDocument)document)
            .ResolveFieldMetadata(Enumerable.Repeat("character/xp", FieldMetadataLimits.MaxBatchSize + 1).ToArray()));
        Assert.Throws<ArgumentException>(() => ((IFieldMetadataDocument)document).ResolveFieldMetadata([""]));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void TargetedAccountAndMarkBindingsFollowClonedRootAcrossConsecutiveWrites()
    {
        using var document = EditCoverageData.Create(false, root =>
        {
            var guardian = GearboxTestData.AddMessage(root, "guardian_rank");
            guardian.SetInteger("guardian_rank", 4);
        }, coordinated: true);
        Assert.Equal(2, ((IFieldMetadataDocument)document).ResolveFieldMetadata(["account/legacy/guardian_rank", "item-0/mark"]).Count);
        var first = document.ApplyEdits([
            new("account/legacy/guardian_rank", FieldValue.Of(5)),
            new("item-0/mark", FieldValue.Of(1)),
        ]);
        Assert.True(first.Succeeded);
        Assert.Equal(5, document.Root.Child("guardian_rank")!.Integer("guardian_rank"));
        Assert.Equal(1, GearboxItemMarks.Get(document.Inventory.Find("item-0").Field.Message!));
        var second = document.ApplyEdits([
            new("account/legacy/guardian_rank", FieldValue.Of(6)),
            new("item-0/mark", FieldValue.Of(2)),
        ]);
        Assert.True(second.Succeeded);
        Assert.Equal(6, document.Root.Child("guardian_rank")!.Integer("guardian_rank"));
        Assert.Equal(2, GearboxItemMarks.Get(document.Inventory.Find("item-0").Field.Message!));
        var changed = document.Serialize().Single().Content.ToArray();
        var undo = document.RestoreMemento(second.Revert!);
        Assert.Equal(5, document.Root.Child("guardian_rank")!.Integer("guardian_rank"));
        Assert.Equal(1, GearboxItemMarks.Get(document.Inventory.Find("item-0").Field.Message!));
        _ = document.RestoreMemento(undo);
        Assert.Equal(changed, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void QualifiedForeignItemCollectionsAreOmittedAndValidAliasesAreReadOnly()
    {
        using var document = EditCoverageData.Create(false);
        var metadata = ((IFieldMetadataDocument)document).ResolveFieldMetadata([
            "bank_inventory_list/item-0/level", "inventory_items/item-0/level"]);

        var field = Assert.Single(metadata);
        Assert.Equal("inventory_items/item-0/level", field.ValueId);
        Assert.Equal("item-0/level", field.CanonicalValueId);
        Assert.Equal(FieldBindingKind.Derived, field.BindingKind);
        Assert.True(field.Spec.ReadOnly);
        var before = document.Serialize().Single().Content.ToArray();
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(3))]).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void MalformedNumericAndForeignItemIdsAreOmittedWithoutDuplicatePrimaries()
    {
        using var document = EditCoverageData.Create(false);
        var before = document.Serialize().Single().Content.ToArray();
        var metadata = ((IFieldMetadataDocument)document).ResolveFieldMetadata([
            "item-00/level", "item-0/level", "ammo/+0", "ammo/0", "ammo/ 0", "foreign:item-0/level"]);

        Assert.Equal(["item-0/level", "ammo/0"], metadata.Select(field => field.ValueId));
        Assert.All(metadata, field => Assert.Equal(FieldBindingKind.Primary, field.BindingKind));
        Assert.False(document.ApplyEdits([new("item-00/level", FieldValue.Of(2))]).Succeeded);
        Assert.False(document.ApplyEdits([new("foreign:item-0/level", FieldValue.Of(2))]).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());

        var old = document.GetValue("item-0/level");
        var applied = document.ApplyEdits([new("item-0/level", FieldValue.Of(old.AsInt32() + 1))]);
        Assert.True(applied.Succeeded);
        var changed = document.Serialize().Single().Content.ToArray();
        var redo = document.RestoreMemento(applied.Revert!);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        _ = document.RestoreMemento(redo);
        Assert.Equal(changed, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void ColdTargetedLookupStopsBeforeUnrelatedFieldsAndLargeRepeatedLists()
    {
        using var document = EditCoverageData.Create(false, root =>
        {
            for (var i = 0; i < GearboxInventory.DirectLookupWorkLimit + 800; i++)
                root.Add(new ProtoField(1900, 0, null, [], 0UL, null));
            for (var i = 0; i < GearboxInventory.DirectLookupWorkLimit + 800; i++)
                EditCoverageData.AddPool(root, EditCoverageData.OtherAmmoPath, i);
        });
        var before = document.Serialize().Single().Content.ToArray();

        var metadata = ((IFieldMetadataDocument)document).ResolveFieldMetadata(["item-4095/level", "ammo/4095"]);

        Assert.Empty(metadata);
        Assert.True(document.Inventory.LastDirectLookupExceeded);
        Assert.InRange(document.Inventory.LastDirectFieldsVisited, 1, GearboxInventory.DirectLookupWorkLimit + 1);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void EmptyKnownInventoryCountsRemainInspectable()
    {
        using var document = EditCoverageData.Create(false, profile: true);
        var metadata = ((IFieldMetadataDocument)document).ResolveFieldMetadata([
            "bank_inventory_list/count", "lost_loot_inventory_list/count"]);

        Assert.Equal(["bank_inventory_list/count", "lost_loot_inventory_list/count"], metadata.Select(field => field.ValueId));
        Assert.All(metadata, field => Assert.Equal(0, document.GetValue(field.ValueId).AsInt64()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupMetadataQualifiesMembersAndUsesCurrentCandidateAfterRestore(bool wonderlands)
    {
        using var group = GroupProtocolTests.Open(wonderlands);
        var original = group.Serialize().Select(output => output.Content.ToArray()).ToArray();
        var metadata = ((IFieldMetadataDocument)group).ResolveFieldMetadata(
            ["profile/unknown", "character/character/xp", "unknown/id"]);

        Assert.Equal(["character/character/xp"], metadata.Select(field => field.ValueId));
        Assert.Equal("character/character/xp", metadata[0].CanonicalValueId);
        Assert.All(metadata, field => Assert.Equal(FieldBindingKind.Primary, field.BindingKind));

        var xp = group.Character.Root.Integer("experience_points");
        Assert.True(group.ApplyEdits([new("character/character/xp", FieldValue.Of(xp + 1))]).Succeeded);
        Assert.Equal(xp + 1, group.GetValue("character/character/xp").AsInt64());
        _ = group.RestoreMemento(group.CaptureMemento());
        Assert.IsType<IntegerSpec>(((IFieldMetadataDocument)group)
            .ResolveFieldMetadata(["character/character/xp"]).Single().Spec);
        Assert.Equal(xp + 1, group.GetValue("character/character/xp").AsInt64());
        Assert.False(original[0].AsSpan().SequenceEqual(group.Serialize().First().Content.Span));
    }
}
