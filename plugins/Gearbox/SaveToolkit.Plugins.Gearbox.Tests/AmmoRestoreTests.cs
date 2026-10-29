using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class AmmoRestoreTests
{
    private static readonly Dictionary<string, FieldValue> Empty = [];
    private const string Action = "ammo/restore-imported";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_PreservesOpeningBaselineAcrossEditsUndoRedoAndHigherValues(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands);
        var imported = document.Serialize().Single().Content.ToArray();
        Assert.Empty(document.PlanAction(Action, Empty, new()).Changes);
        _ = document.Query("ammo", new());
        Assert.True(document.ApplyEdits([new("ammo/0", FieldValue.Of(5.0)), new("ammo/1", FieldValue.Of(75.0))]).Succeeded);
        _ = document.Query("ammo", new());
        Assert.True(document.ApplyEdits([new("ammo/0", FieldValue.Of(1.0))]).Succeeded);
        var before = document.Serialize().Single().Content.ToArray();
        var plan = document.PlanAction(Action, Empty, new());
        Assert.Empty(plan.Errors);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("ammo/0", change.ValueId);
        Assert.Equal(FieldValue.Of(1.0), change.OldValue);
        Assert.Equal(FieldValue.Of(123.5), change.NewValue);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        var outcome = document.ApplyActionPlan(Action, Empty, new(), plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(75, document.Root.Children("resource_pools").ElementAt(1).Float("amount"));
        EditCoverageTests.AssertHistory(document, outcome, before);
        Assert.Empty(document.PlanAction(Action, Empty, new()).Changes);
        var output = document.Serialize().Single().Content.ToArray();
        var container = GearboxContainer.Parse(output, wonderlands ? "PPSA01493" : "PPSA01463", false);
        using var reopened = new GearboxDocument("1", wonderlands ? "PPSA01493" : "PPSA01463", container, container.Decode(), SaveToolkit.Sdk.References.IReferenceContext.Empty);
        _ = reopened.Query("ammo", new());
        Assert.True(reopened.ApplyEdits([new("ammo/1", FieldValue.Of(60.0))]).Succeeded);
        Assert.Equal(FieldValue.Of(75.0), Assert.Single(reopened.PlanAction(Action, Empty, new()).Changes).NewValue);
        Assert.NotEqual(imported, output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_SkipsUnknownDuplicateMissingAndOutOfPolicyPools(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            EditCoverageData.AddPool(root, "unknown", 40);
            EditCoverageData.AddPool(root, EditCoverageData.OtherAmmoPath, 10);
            EditCoverageData.AddPool(root, "/Game/GameData/Weapons/Ammo/Resource_Ammo_Heavy.Resource_Ammo_Heavy", 100_001);
        });
        foreach (var pool in document.Root.Children("resource_pools")) pool.SetFloat("amount", 1);
        var plan = document.PlanAction(Action, Empty, new());
        Assert.Empty(plan.Errors);
        Assert.Single(plan.Changes);
        Assert.Equal(4, plan.SkippedRows.Count);
        var before = document.Serialize().Single().Content.ToArray();
        Assert.False(document.ApplyActionPlan(Action, Empty, new(), plan with { Warnings = [] }).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.Empty(document.PlanAction(Action, Empty, new([])).Changes);
        Assert.Single(document.PlanAction(Action, Empty, new(["0"])).Changes);
        Assert.NotEmpty(document.PlanAction(Action, Empty, new(["item-0"])).Errors);
        var scope = new ActionScope(["0"]) { QueryScope = new("inventory_items", new(), 0) };
        Assert.NotEmpty(document.PlanAction(Action, Empty, scope).Errors);
        document.Root.Children("resource_pools").First().SetFloat("amount", -1);
        Assert.Empty(document.PlanAction(Action, Empty, new(["0"])).Changes);
        Assert.Single(document.PlanAction(Action, Empty, new(["0"])).SkippedRows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_SignedZeroAndNonfiniteBaselinesAreNoOps(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            root.Children("resource_pools").First().SetFloat("amount", -0.0f);
            root.Children("resource_pools").ElementAt(1).SetFloat("amount", float.NaN);
        });
        var bytes = document.Serialize().Single().Content.ToArray();
        var plan = document.PlanAction(Action, Empty, new());
        Assert.Empty(plan.Changes);
        Assert.True(document.ApplyActionPlan(Action, Empty, new(), plan).Succeeded);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
        using var profile = EditCoverageData.Create(wonderlands, profile: true);
        Assert.NotEmpty(profile.PlanAction(Action, Empty, new()).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restore_NeverCreatesMissingAmountsOrReusesChangedPoolIdentities(bool wonderlands)
    {
        using var document = EditCoverageData.Create(wonderlands, root =>
        {
            var pool = root.Children("resource_pools").First();
            pool.Remove(pool.Last("amount")!);
        });
        var current = document.Root.Children("resource_pools").ToArray();
        current[0].SetFloat("amount", 1);
        current[1].Remove(current[1].Last("amount")!);
        var before = document.Serialize().Single().Content.ToArray();
        var plan = document.PlanAction(Action, Empty, new());
        Assert.Empty(plan.Changes);
        Assert.Equal(2, plan.SkippedRows.Count);
        Assert.True(document.ApplyActionPlan(Action, Empty, new(), plan).Succeeded);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        current[1].SetText("resource_path", EditCoverageData.AmmoPath);
        current[1].SetFloat("amount", 1);
        Assert.Empty(document.PlanAction(Action, Empty, new(["1"])).Changes);
        Assert.NotEmpty(document.PlanAction(Action, Empty, new(["0", "0"])).Errors);
    }
}
