using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GroupScopeTests
{
    private static readonly Dictionary<string, FieldValue> Empty = [];
    private const string ItemRow = "character/inventory_items/item-0";
    private const string MatchLevel = "character/items/match-level";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolvedQueryScopes_PreserveInventoryValidationAndExclusionContract(bool wonderlands)
    {
        using var group = GroupProtocolTests.Open(wonderlands);
        var adapter = new DocumentMemberAdapter("character", group.Character, GearboxGroupIdentity.Policy);
        var query = new ActionQueryScope("character/inventory_items", new() { Limit = 1 }, 17)
        { ExcludedRowKeys = ["character/inventory_items/item-1"] };
        var scope = new ActionScope([ItemRow]) { QueryScope = query };
        var local = adapter.ToLocalScope(scope);
        Assert.Equal(["item-0"], local.RowKeys);
        Assert.Equal(query with { CollectionId = "inventory_items" }, local.QueryScope);
        Assert.True(ActionPlans.ContentEquals(group.PlanAction(MatchLevel, Empty, new([ItemRow])), group.PlanAction(MatchLevel, Empty, scope)));
        Assert.Null(adapter.ToLocalScope(new()).RowKeys);
        Assert.Empty(adapter.ToLocalScope(new([])).RowKeys!);

        var ammo = new ActionScope(["character/ammo/0"]) { QueryScope = new("character/ammo", new(), 19) };
        Assert.Equal(["0"], adapter.ToLocalScope(ammo).RowKeys);
        Assert.Equal("ammo", adapter.ToLocalScope(ammo).QueryScope!.CollectionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignStaleAndWrongCollectionScopes_RejectWithoutChangingBytes(bool wonderlands)
    {
        using var group = GroupProtocolTests.Open(wonderlands);
        var before = Bytes(group);
        Assert.Throws<KeyNotFoundException>(() => group.PlanAction(MatchLevel, Empty, new(["profile/inventory_items/item-0"])));
        Assert.Throws<SaveFormatException>(() => group.PlanAction(MatchLevel, Empty, new(["character/item-0"])));
        Assert.Throws<SaveFormatException>(() => group.PlanAction(MatchLevel, Empty, new(["character/bank_inventory_list/item-0"])));
        Assert.Throws<SaveFormatException>(() => group.PlanAction(MatchLevel, Empty,
            new([ItemRow]) { QueryScope = new("character/bank_inventory_list", new(), 0) }));
        Assert.Throws<KeyNotFoundException>(() => group.PlanAction(MatchLevel, Empty,
            new([ItemRow]) { QueryScope = new("profile/bank_inventory_list", new(), 0) }));
        Assert.Throws<SaveFormatException>(() => group.PlanAction("character/ammo/restore-imported", Empty,
            new(["character/ammo/0"]) { QueryScope = new("character/inventory_items", new(), 0) }));
        Assert.Throws<SaveFormatException>(() => group.PlanAction(MatchLevel, Empty, new(["character/inventory_items/missing"])));
        Assert.NotEmpty(group.PlanAction(MatchLevel, Empty, new([ItemRow, ItemRow])).Errors);
        AssertBytes(group, before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedMemberBatchAndUnknownOwner_RestoreAndRebindAllDispatch(bool wonderlands)
    {
        using var group = GroupProtocolTests.Open(wonderlands);
        _ = group.BuildView();
        var before = Bytes(group);
        var xp = group.Character.Root.Integer("experience_points");
        var failed = group.ApplyEdits([new("character/character/xp", FieldValue.Of(xp + 1)), new("profile/absent", FieldValue.Of(1))]);
        Assert.False(failed.Succeeded);
        Assert.All(failed.Errors, error => Assert.StartsWith("profile/", error.ValueId, StringComparison.Ordinal));
        AssertBytes(group, before);
        _ = group.BuildView();
        Assert.Throws<KeyNotFoundException>(() => group.ApplyEdits(
            [new("character/character/xp", FieldValue.Of(xp + 1)), new("foreign/value", FieldValue.Of(1))]));
        AssertBytes(group, before);

        _ = group.BuildView();
        Assert.True(group.ApplyEdits([new("character/character/level", FieldValue.Of(17))]).Succeeded);
        Assert.Equal(17, group.GetValue("character/character/level").AsInt32());
        var plan = group.PlanAction(MatchLevel, Empty, new([ItemRow]));
        Assert.Empty(plan.Errors);
        Assert.NotEmpty(plan.Changes);
        Assert.All(plan.Changes, change => Assert.Equal(FieldValue.Of(17), change.NewValue));
        Assert.True(group.ApplyActionPlan(MatchLevel, Empty, new([ItemRow]), plan).Succeeded);
    }

    private static Dictionary<string, byte[]> Bytes(GearboxGroupDocument group) =>
        group.Serialize().ToDictionary(output => output.RelativePath, output => output.Content.ToArray());

    private static void AssertBytes(GearboxGroupDocument group, IReadOnlyDictionary<string, byte[]> expected) =>
        Assert.All(group.Serialize(), output => Assert.Equal(expected[output.RelativePath], output.Content.ToArray()));
}
