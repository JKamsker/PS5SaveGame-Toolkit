using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class FieldMetadataR6SessionTests
{
    [Fact]
    public void ApprovedAccountItemAndStatisticEditsKeepRealGroupContextAndHistoryAtomic()
    {
        using var character = EditCoverageData.Create(true, root =>
        {
            var hero = GearboxTestData.AddMessage(root, "hero_points_save_data");
            hero.SetInteger("strength", 7);
            hero.SetInteger("respec_count", 3);
            for (var i = 0; i < 2; i++)
            {
                var stat = GearboxTestData.AddMessage(root, "game_stats_data");
                stat.SetText("stat_path", CharacterProgression.LevelStat(true));
                stat.SetInteger("stat_value", 20);
            }
        });
        using var profile = EditCoverageData.Create(true, root =>
        {
            var myth = GearboxTestData.AddMessage(root, "player_prestige");
            myth.SetInteger("prestige_experience", 10);
            myth.SetInteger("has_seen_tutorial", 0);
        }, profile: true);
        var originalLevel = Assert.Single(character.ResolveFieldMetadata(["item-0/level"]));
        var level = character.GetValue(originalLevel.ValueId).AsInt32();
        using var workspace = MetadataR6Data.Workspace(MetadataR6Data.Set(character, profile));
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "1");
        var group = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, "gearbox")).Token);
        var session = group.Session;
        var beforeState = session.State;
        var baseline = session.GetChanges(TestContext.Current.CancellationToken);
        Assert.Empty(baseline.Changes);
        Assert.Equal(beforeState, session.State);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        foreach (var id in new[] { "character/account/hero/respec_count", "profile/account/myth/has_seen_tutorial",
            "character/bank_inventory_list/count", "profile/inventory_items/count" })
        {
            Assert.False(session.ApplyEdits([Edit(id, 2)], expectedRevision: null,
                cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(beforeState, session.State);
        }
        var applied = session.ApplyEdits([
            Edit("character/account/hero/strength", 8), Edit("profile/account/myth/prestige_experience", 11),
            Edit("character/character/xp", character.Root.Schema.Experience[20]), Edit("character/item-0/level", level + 1)],
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(applied.Ok);
        Assert.True(session.State.IsDirty);
        Assert.True(session.State.CanUndo);
        var changed = session.Serialize(TestContext.Current.CancellationToken)
            .ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
        Assert.Equal(2, changed.Count);
        var reopenedCharacter = GearboxContainer.Parse(changed.Single(output => output.Key.EndsWith("/1", StringComparison.Ordinal)).Value,
            "PPSA01493", false).Decode();
        var reopenedProfile = GearboxContainer.Parse(changed.Single(output => output.Key.EndsWith("/profile", StringComparison.Ordinal)).Value,
            "PPSA01493", true).Decode();
        Assert.Equal(8, reopenedCharacter.Child("hero_points_save_data")!.Integer("strength"));
        Assert.Equal(11, reopenedProfile.Child("player_prestige")!.Integer("prestige_experience"));
        Assert.All(reopenedCharacter.Children("game_stats_data"), stat => Assert.Equal(21, stat.Integer("stat_value")));
        var serial = InventoryEntry.SerialOf(reopenedCharacter.All(GearboxInventory.Backpack).First());
        _ = IndependentSerialProbe.Decode(serial);
        Assert.Equal(level + 1, new SerialReader(true).Read(serial).Level);
        var committedState = session.State;
        var review = session.GetChanges(TestContext.Current.CancellationToken);
        var missingContexts = new List<string>();
        foreach (var id in new[] { "character/account/hero/strength", "profile/account/myth/prestige_experience",
            "character/item-0/level", "character/character/recorded-level", "character/character/recorded-level/1" })
        {
            var rows = review.Changes.Where(row => row.ValueId == id).ToArray();
            if (rows.Length != 1)
            {
                missingContexts.Add(id + " (expected one row, actual " + rows.Length + ")");
                continue;
            }
            var change = rows[0];
            if (change.Member is { } context)
            {
                Assert.Contains(group.Members, member => member.Role == context.Role && member.RelativePath == context.RelativePath);
                Assert.Null(context.Provenance);
            }
            else missingContexts.Add(id);
        }
        Assert.DoesNotContain(review.Changes, row => row.ValueId == "character/character/recorded-level/0");
        Assert.Equal(committedState, session.State);
        SelectionByteAssertions.Equal(changed, session.Serialize(TestContext.Current.CancellationToken)
            .ToDictionary(output => output.RelativePath, output => output.Content.ToArray()));
        Assert.False(session.ApplyEdits([Edit("character/account/hero/strength", 9),
            Edit("profile/account/myth/has_seen_tutorial", 2)], expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.ApplyEdits([Edit("character/item-0/level", level + 2)],
            expectedRevision: null, cancellationToken: cancelled.Token));
        Assert.Equal(committedState, session.State);
        SelectionByteAssertions.Equal(changed, session.Serialize(TestContext.Current.CancellationToken)
            .ToDictionary(output => output.RelativePath, output => output.Content.ToArray()));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.True(session.State.CanRedo);
        Assert.Empty(session.GetChanges(TestContext.Current.CancellationToken).Changes);
        Assert.True(session.Redo().Ok);
        SelectionByteAssertions.Equal(changed, session.Serialize(TestContext.Current.CancellationToken)
            .ToDictionary(output => output.RelativePath, output => output.Content.ToArray()));
        Assert.NotEmpty(session.GetChanges(TestContext.Current.CancellationToken).Changes);
        Assert.True(missingContexts.Count == 0, "Missing member context for " + string.Join(", ", missingContexts) +
            "; warnings: " + string.Join(" | ", review.Warnings) + "; canonical IDs: " +
            string.Join(", ", review.Changes.Select(change => change.ValueId + "=" + (change.CanonicalValueId ?? "<null>"))));
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value));
}
