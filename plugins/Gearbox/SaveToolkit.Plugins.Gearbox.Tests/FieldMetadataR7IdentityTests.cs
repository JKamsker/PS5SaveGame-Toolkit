using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class FieldMetadataR7IdentityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CategoryRemovalKeepsAppendedItemAndSiblingIdentityInBothOrders(bool wonderlands, bool moneyFirst)
    {
        using var document = MetadataR7Data.Character(wonderlands);
        MetadataR7Data.Prepare(document);
        _ = document.RestoreMemento(document.CaptureState());
        Assert.Equal(2, document.ResolveFieldMetadata([MetadataR7Data.Money, "item-3/level"]).Count);
        var sibling = document.Inventory.Find("item-4").Field.Encode();
        var before = SelectionTestData.Bytes(document);
        var outcome = document.ApplyEdits(MetadataR7Data.Edits(moneyFirst));
        MetadataR7Data.AssertChanges(outcome);
        var changed = SelectionTestData.Bytes(document);
        MetadataR7Data.AssertReopened(Assert.Single(changed).Value, wonderlands, sibling);
        MetadataR7Data.AssertWholeOutput(Assert.Single(before).Value, Assert.Single(changed).Value, wonderlands);
        var redo = document.RestoreMemento(outcome.Revert!);
        SelectionTestData.AssertBytes(document, before);
        Assert.Empty(Assert.Single(document.Root.All("inventory_category_list")).Original);
        _ = document.RestoreMemento(redo);
        SelectionTestData.AssertBytes(document, changed);
        Assert.Single(document.ResolveFieldMetadata(["item-3/level"]));
        Assert.Equal(15, document.GetValue("item-3/level").AsInt32());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FreshGroupRestoreKeepsNewCategoryProvenanceAndExactMemberHistory(bool wonderlands, bool moneyFirst)
    {
        using var character = MetadataR7Data.Character(wonderlands);
        using var profile = EditCoverageData.Create(wonderlands, profile: true);
        var set = MetadataR6Data.Set(character, profile);
        using var original = MetadataR6Data.Group(set);
        MetadataR7Data.Prepare(original.Character);
        using var group = MetadataR6Data.Group(set);
        _ = group.RestoreMemento(original.CaptureMemento());
        Assert.Empty(Assert.Single(group.Character.Root.All("inventory_category_list")).Original);
        var before = SelectionTestData.Bytes(group);
        var sibling = group.Character.Inventory.Find("item-4").Field.Encode();
        Assert.Equal(2, group.ResolveFieldContext(["character/" + MetadataR7Data.Money, "character/item-3/level"]).Count);
        var outcome = group.ApplyEdits(MetadataR7Data.Edits(moneyFirst, "character/"));
        MetadataR7Data.AssertChanges(outcome, "character/");
        var changed = SelectionTestData.Bytes(group);
        var characterPath = changed.Keys.Single(path => path.EndsWith("/1", StringComparison.Ordinal));
        var profilePath = changed.Keys.Single(path => path.EndsWith("/profile", StringComparison.Ordinal));
        MetadataR7Data.AssertReopened(changed[characterPath], wonderlands, sibling);
        MetadataR7Data.AssertWholeOutput(before[characterPath], changed[characterPath], wonderlands);
        Assert.Equal(before[profilePath], changed[profilePath]);
        var redo = group.RestoreMemento(outcome.Revert!);
        SelectionTestData.AssertBytes(group, before);
        _ = group.RestoreMemento(redo);
        SelectionTestData.AssertBytes(group, changed);
        Assert.Single(group.ResolveFieldContext(["character/item-3/level"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealStagedSessionPreservesAppendedIdentityThroughScalarRemovalAndUndo(bool moneyFirst)
    {
        using var character = MetadataR7Data.Character(false);
        using var profile = EditCoverageData.Create(false, profile: true);
        using var workspace = MetadataR6Data.Workspace(MetadataR6Data.Set(character, profile));
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "1");
        var session = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, "gearbox")).Token).Session;
        _ = session.BuildView();
        var created = session.ApplyEdits([Edit("character/" + MetadataR7Data.Money, 100)], null, Ct);
        Assert.True(created.Ok, string.Join(" | ", created.Errors.Select(error => error.Message)));
        var action = SelectionTestData.DiscoverItemAction(session, "character/inventory_items", "character/inventory_items/item-0", 0,
            "character/items/duplicate");
        var preview = session.PlanAction(action.ActionId, new Dictionary<string, JsonElement>(),
            new(["character/inventory_items/item-0", "character/inventory_items/item-1"]), null, Ct);
        Assert.Empty(preview.Plan.Errors);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, Ct).Ok);
        var before = Bytes(session);
        var path = before.Keys.Single(path => path.EndsWith("/1", StringComparison.Ordinal));
        var model = GearboxContainer.Parse(before[path], "PPSA01463", false).Decode();
        var sibling = model.All(GearboxInventory.Backpack).ElementAt(4).Encode();
        var edits = MetadataR7Data.Edits(moneyFirst, "character/")
            .Select(edit => Edit(edit.ValueId, edit.Value.AsInt32())).ToArray();
        Assert.True(session.ApplyEdits(edits, null, Ct).Ok);
        var changed = Bytes(session);
        MetadataR7Data.AssertReopened(changed[path], false, sibling);
        MetadataR7Data.AssertWholeOutput(before[path], changed[path], false);
        Assert.Equal(before.Keys.Order(), changed.Keys.Order());
        foreach (var member in before.Keys.Where(member => member != path)) Assert.Equal(before[member], changed[member]);
        Assert.True(session.Undo().Ok);
        SelectionByteAssertions.Equal(before, Bytes(session));
        Assert.True(session.Redo().Ok);
        SelectionByteAssertions.Equal(changed, Bytes(session));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value));
    private static Dictionary<string, byte[]> Bytes(DocumentSession session) => session.Serialize(Ct)
        .ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
}
