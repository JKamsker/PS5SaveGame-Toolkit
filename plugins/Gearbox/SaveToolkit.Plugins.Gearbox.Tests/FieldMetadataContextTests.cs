using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class FieldMetadataContextTests
{
    [Fact]
    public void ContextUsesSelectedRolesAndPathsInRequestedOrderWithoutMutation()
    {
        using var group = GroupProtocolTests.Open(false);
        var before = group.Serialize().Select(output => output.Content.ToArray()).ToArray();
        var beforeMemento = group.CaptureMemento().SizeBytes;
        var contexts = ((IGroupedFieldContext)group).ResolveFieldContext([
            "profile/bank_inventory_list/count", "character/character/xp", "character/character/xp",
            "profile/not-a-real-field", "foreign/character/xp"]);

        Assert.Equal(["profile/bank_inventory_list/count", "character/character/xp"],
            contexts.Select(context => context.ValueId));
        var profile = Assert.Single(contexts, context => context.Role == "profile");
        var character = Assert.Single(contexts, context => context.Role == "character");
        Assert.EndsWith("/profile", profile.RelativePath, StringComparison.Ordinal);
        Assert.EndsWith("/1", character.RelativePath, StringComparison.Ordinal);
        Assert.Null(profile.Provenance);
        Assert.Null(character.Provenance);
        Assert.Equal(beforeMemento, group.CaptureMemento().SizeBytes);
        Assert.Equal(before, group.Serialize().Select(output => output.Content.ToArray()).ToArray());
    }

    [Fact]
    public void ContextFollowsCurrentCandidateAfterEditUndoRestoreAndFailedEdit()
    {
        using var group = GroupProtocolTests.Open(false);
        var id = "character/character/xp";
        var initial = group.CaptureMemento();
        Assert.Single(((IGroupedFieldContext)group).ResolveFieldContext([id]));
        var original = group.Character.Root.Integer("experience_points");

        var applied = group.ApplyEdits([new(id, FieldValue.Of(original + 1))]);
        Assert.True(applied.Succeeded);
        Assert.Single(((IGroupedFieldContext)group).ResolveFieldContext([id]));

        var rejected = group.ApplyEdits([new(id, FieldValue.Of(-1))]);
        Assert.False(rejected.Succeeded);
        Assert.Single(((IGroupedFieldContext)group).ResolveFieldContext([id]));

        _ = group.RestoreMemento(initial);
        Assert.Equal(original, group.Character.Root.Integer("experience_points"));
        Assert.Single(((IGroupedFieldContext)group).ResolveFieldContext([id]));
    }

    [Fact]
    public void ContextOmitsRetiredAndUnownedIdentitiesAndRejectsMalformedBatches()
    {
        using var group = GroupProtocolTests.Open(false);
        Assert.Empty(((IGroupedFieldContext)group).ResolveFieldContext([
            "character/not-a-real-field", "profile/character/xp", "synthetic/character/xp"]));

        var entry = group.Character.Inventory.Entries.First();
        var localId = "inventory_items/" + entry.Id + "/level";
        var groupId = "character/" + localId;
        Assert.Single(((IGroupedFieldContext)group).ResolveFieldContext([groupId]));
        var scope = new ActionScope(["character/inventory_items/" + entry.Id]);
        var plan = group.PlanAction("character/items/remove", new Dictionary<string, FieldValue>(), scope);
        Assert.Empty(plan.Errors);
        Assert.True(group.ApplyActionPlan("character/items/remove", new Dictionary<string, FieldValue>(), scope, plan).Succeeded);
        Assert.Empty(((IGroupedFieldContext)group).ResolveFieldContext([groupId]));

        Assert.Throws<ArgumentException>(() => ((IGroupedFieldContext)group).ResolveFieldContext([""]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((IGroupedFieldContext)group)
            .ResolveFieldContext(Enumerable.Repeat(groupId, FieldMetadataLimits.MaxBatchSize + 1).ToArray()));
    }

    [Fact]
    public void SessionReviewCarriesActualMemberContextAfterRealGroupEdit()
    {
        using var workspace = SelectionTestData.Workspace(false);
        var character = workspace.Set.AllDiskFiles.Single(file => file.FileName == "1");
        var session = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(character.Id, "gearbox")).Token).Session;
        var id = "character/character/xp";
        using var probe = SelectionTestData.Open(false, profile: false);
        var next = probe.Root.Integer("experience_points") + 1;
        var result = session.ApplyEdits([new(id, JsonSerializer.SerializeToElement(next))], expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Ok);
        var revision = session.State.Revision;
        var beforeReview = session.Serialize(cancellationToken: TestContext.Current.CancellationToken)
            .ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
        var review = session.GetChanges(TestContext.Current.CancellationToken);
        var change = Assert.Single(review.Changes, item => item.ValueId == id);
        Assert.Equal("character", change.Member!.Role);
        Assert.EndsWith("/1", change.Member.RelativePath, StringComparison.Ordinal);
        Assert.Equal(revision, session.State.Revision);
        Assert.Equal(beforeReview, session.Serialize(cancellationToken: TestContext.Current.CancellationToken)
            .ToDictionary(output => output.RelativePath, output => output.Content.ToArray()));
    }
}
