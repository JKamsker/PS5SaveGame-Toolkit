using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class FieldMetadataR7LookupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColdRootScalarsUseLastValueAndPolicyWithoutScanningUnknownTail(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            root.SetText("preferred_character_name", "Early");
            root.SetInteger("name_character_limit", 5);
            root.SetInteger("playthroughs_completed", 1);
            MetadataR7Data.UnknownTail(root);
            Duplicate(root, "preferred_character_name", "Last name");
            Duplicate(root, "name_character_limit", 12UL);
            Duplicate(root, "playthroughs_completed", 2UL);
        });
        var construction = document.Root.IndexConstructionVisits;
        Assert.True(construction > 5000);
        var reads = document.Root.IndexedFieldReads;
        var visits = document.Root.TargetedFieldVisits;
        var metadata = document.ResolveFieldMetadata(["character/name", "world/completed", "not-a-field"]);
        Assert.Equal(["character/name", "world/completed"], metadata.Select(field => field.ValueId));
        Assert.Equal(12, Assert.IsType<TextSpec>(metadata[0].Spec).MaxLength);
        Assert.Equal("Last name", document.GetValue("character/name").AsString());
        Assert.Equal(2, document.GetValue("world/completed").AsInt32());
        Assert.InRange(document.Root.IndexedFieldReads - reads, 1, 12);
        Assert.Equal(visits, document.Root.TargetedFieldVisits);
        Assert.Equal(construction, document.Root.IndexConstructionVisits);
        var before = SelectionTestData.Bytes(document);
        var edit = document.ApplyEdits([new("character/name", FieldValue.Of("Changed"))]);
        Assert.True(edit.Succeeded);
        reads = document.Root.IndexedFieldReads;
        Assert.Equal("Changed", document.GetValue("character/name").AsString());
        Assert.Equal(1, document.Root.IndexedFieldReads - reads);
        var changed = SelectionTestData.Bytes(document);
        var redo = document.RestoreMemento(edit.Revert!);
        Assert.Single(document.ResolveFieldMetadata(["character/name"]));
        Assert.Equal("Last name", document.GetValue("character/name").AsString());
        SelectionTestData.AssertBytes(document, before);
        _ = document.RestoreMemento(redo);
        SelectionTestData.AssertBytes(document, changed);
    }

    [Theory]
    [InlineData(false, false, "legacy")]
    [InlineData(false, true, "profile")]
    [InlineData(true, false, "hero")]
    [InlineData(true, true, "myth")]
    [InlineData(true, false, "myth-mirror")]
    public void NestedAccountLastValueAndLiveClonesUseMaintainedModelIndex(bool wonderlands, bool profile, string role)
    {
        var field = role == "hero" ? "strength" : wonderlands ? "prestige_experience" : "guardian_rank";
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            var parent = root;
            if (role == "myth-mirror")
            {
                parent = GearboxTestData.AddMessage(root, "profile_cloud_data");
                MetadataR7Data.UnknownTail(parent);
            }
            parent = GearboxTestData.AddMessage(parent, role == "hero" ? "hero_points_save_data" :
                wonderlands ? "player_prestige" : "guardian_rank");
            parent.SetInteger(field, 3);
            MetadataR7Data.UnknownTail(parent);
            Duplicate(parent, field, 9UL);
            MetadataR7Data.UnknownTail(root);
        }, profile, coordinated: true);
        var id = "account/" + role + "/" + field;
        var parentModel = Parent(document.Root, role, wonderlands);
        var rootReads = document.Root.IndexedFieldReads;
        var parentReads = parentModel.IndexedFieldReads;
        var rootConstruction = document.Root.IndexConstructionVisits;
        var parentConstruction = parentModel.IndexConstructionVisits;
        Assert.Single(document.ResolveFieldMetadata([id, "account/" + role + "/respec_count"]));
        Assert.Equal(9, document.GetValue(id).AsInt32());
        Assert.InRange(document.Root.IndexedFieldReads - rootReads, 1, 16);
        Assert.InRange(parentModel.IndexedFieldReads - parentReads, 1, 8);
        Assert.Equal(0, document.Root.TargetedFieldVisits);
        Assert.Equal(0, parentModel.TargetedFieldVisits);
        Assert.Equal(rootConstruction, document.Root.IndexConstructionVisits);
        Assert.Equal(parentConstruction, parentModel.IndexConstructionVisits);
        var before = SelectionTestData.Bytes(document);
        var outcome = document.ApplyEdits([new(id, FieldValue.Of(10))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(10, document.GetValue(id).AsInt32());
        var changed = SelectionTestData.Bytes(document);
        var decoded = GearboxContainer.Parse(Assert.Single(changed).Value, wonderlands ? "PPSA01493" : "PPSA01463", profile).Decode();
        Assert.Equal(10, Parent(decoded, role, wonderlands).Integer(field));
        var redo = document.RestoreMemento(outcome.Revert!);
        Assert.Single(document.ResolveFieldMetadata([id]));
        Assert.Equal(9, document.GetValue(id).AsInt32());
        SelectionTestData.AssertBytes(document, before);
        _ = document.RestoreMemento(redo);
        SelectionTestData.AssertBytes(document, changed);
    }

    [Fact]
    public void UnestablishedLateRepeatedRecordRemainsBoundedWhileLiveBindingSurvivesClone()
    {
        using var document = EditCoverageData.Create(false, root =>
        {
            MetadataR7Data.UnknownTail(root);
            GearboxTestData.AddMessage(root, "game_state_save_data_for_playthrough").SetInteger("mayhem_level", 3);
        });
        Assert.Empty(document.ResolveFieldMetadata(["world/0/difficulty"]));
        Assert.Equal(GearboxInventory.DirectLookupWorkLimit + 1, document.Root.TargetedFieldVisits);
        _ = document.WorldDifficulty(0, false); // The ordinary rendered factory establishes the known ordinal.
        var outcome = document.ApplyEdits([new("world/0/difficulty", FieldValue.Of(4))]);
        Assert.True(outcome.Succeeded);
        var reads = document.Root.IndexedFieldReads;
        var scans = document.Root.TargetedFieldVisits;
        Assert.Equal(4, document.GetValue("world/0/difficulty").AsInt32());
        Assert.Equal(1, document.Root.IndexedFieldReads - reads);
        Assert.Equal(scans, document.Root.TargetedFieldVisits);
        var redo = document.RestoreMemento(outcome.Revert!);
        _ = document.WorldDifficulty(0, false);
        Assert.Equal(3, document.GetValue("world/0/difficulty").AsInt32());
        _ = document.RestoreMemento(redo);
        _ = document.WorldDifficulty(0, false);
        Assert.Equal(4, document.GetValue("world/0/difficulty").AsInt32());
    }

    private static ProtoMessage Parent(ProtoMessage root, string role, bool wonderlands) => role == "myth-mirror"
        ? root.Child("profile_cloud_data")!.Child("player_prestige")!
        : root.Child(role == "hero" ? "hero_points_save_data" : wonderlands ? "player_prestige" : "guardian_rank")!;

    [Fact]
    public void CurrentGroupIndexedScalarsKeepDefaultPolicyAndObservationPure()
    {
        using var character = EditCoverageData.Create(true, root =>
        {
            root.SetText("preferred_character_name", "R7");
            GearboxTestData.AddMessage(root, "hero_points_save_data").SetInteger("strength", 7);
            MetadataR7Data.UnknownTail(root);
        });
        using var profile = EditCoverageData.Create(true, root =>
        {
            var myth = GearboxTestData.AddMessage(root, "player_prestige");
            myth.SetInteger("prestige_experience", 10);
            MetadataR7Data.UnknownTail(myth);
        }, profile: true);
        using var group = MetadataR6Data.Group(MetadataR6Data.Set(character, profile));
        var bytes = SelectionTestData.Bytes(group);
        var identity = group.Character.Inventory.Capture();
        var visits = group.Character.Root.TargetedFieldVisits;
        var reads = group.Character.Root.IndexedFieldReads;
        var metadata = group.ResolveFieldMetadata(["character/character/name", "profile/account/myth/prestige_experience",
            "character/account/hero/strength", "profile/account/hero/strength"]);
        Assert.Equal(3, metadata.Count);
        Assert.Equal(32, Assert.IsType<TextSpec>(metadata[0].Spec).MaxLength);
        Assert.Equal(metadata.Select(field => field.ValueId), group.ResolveFieldContext(metadata.Select(field => field.ValueId).ToArray())
            .Select(context => context.ValueId));
        Assert.Equal(visits, group.Character.Root.TargetedFieldVisits);
        Assert.InRange(group.Character.Root.IndexedFieldReads - reads, 1, 24);
        Assert.Equal(identity.NextId, group.Character.Inventory.Capture().NextId);
        Assert.Equal(identity.Initialized, group.Character.Inventory.Capture().Initialized);
        SelectionTestData.AssertBytes(group, bytes);
    }

    private static void Duplicate(ProtoMessage message, string name, object value) => message.Add(new ProtoField(
        message.Schema.Tag(message.Type, name), message.Schema.Spec(message.Type, name).WireType,
        message.Schema.Spec(message.Type, name), [], value, null));
}
