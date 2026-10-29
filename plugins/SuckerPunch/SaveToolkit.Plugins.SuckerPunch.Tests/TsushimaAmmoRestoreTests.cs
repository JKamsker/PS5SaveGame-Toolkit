using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaAmmoRestoreTests
{
    internal const string ActionId = "inventory/restore-imported-ammo";
    private const string FireId = "inventory/ArrowFire/quantity";
    private static readonly string[] ExcludedRows = ["ArrowNormal", "Supplies", "TestItem"];
    private static readonly IReadOnlyDictionary<string, FieldValue> NoParameters = new Dictionary<string, FieldValue>();

    [Fact]
    public void PreviewUsesEachOpeningCountAndCancelPreservesStagedBytes()
    {
        var save = new SyntheticSave(yotei: false);
        SetAmmo(save, "ArrowFire", 9);
        save.Repair();
        using var document = save.Open();
        var action = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory")
            .Children.OfType<ActionNode>().Single(node => node.ActionId == ActionId);
        Assert.True(action.SupportsPlanning);
        Assert.Empty(action.Parameters);
        Assert.Contains("Verified byte structure", action.Description);
        Assert.Contains("Experimental", action.Description);
        Assert.True(document.ApplyEdits([new(save.ArrowId, FieldValue.Of(0L)), new(FireId, FieldValue.Of(1L)),
            new(save.ResourceId, FieldValue.Of(0L))]).Succeeded);
        var staged = Bytes(document);
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(ActionId, NoParameters, new());
        Assert.Empty(plan.Errors);
        Assert.Equal(new[] { save.ArrowId, FireId }, plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId));
        Assert.Equal(new long[] { 2, 9 }, plan.Changes.Where(change => !change.Derived).Select(change => change.NewValue.AsInt64()));
        Assert.Equal(5, plan.Changes.Count);
        Assert.All(plan.Changes.Where(change => change.ValueId.EndsWith("-copy", StringComparison.Ordinal)), change => Assert.True(change.Derived));
        Assert.Contains(plan.Changes, change => change.ValueId == "container/word2" && change.Derived);
        var warning = Assert.Single(plan.Warnings);
        Assert.StartsWith("Not validated on console.", warning);
        Assert.Contains("The game may clamp quantities", warning);
        Assert.Contains("immutable opening", warning);
        Assert.Equal(staged, Bytes(document)); // Cancel means discarding this pure preview.
        Assert.Equal(0L, document.GetValue(save.ResourceId).AsInt64());
        Assert.True(planner.ApplyActionPlan(ActionId, NoParameters, new(), plan).Succeeded);
        Assert.Equal(2L, document.GetValue(save.ArrowId).AsInt64());
        Assert.Equal(9L, document.GetValue(FireId).AsInt64());
        Assert.Equal(0L, document.GetValue(save.ResourceId).AsInt64());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32767)]
    public void BaselineBoundariesAndHigherValuesNeverLowerCounts(uint opening)
    {
        var save = new SyntheticSave(yotei: false);
        SetAmmo(save, "ArrowNormal", opening);
        save.Repair();
        using var document = save.Open();
        var staged = opening == 0 ? 1U : opening - 1;
        Assert.True(document.ApplyEdits([new(save.ArrowId, FieldValue.Of((ulong)staged))]).Succeeded);
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(["ArrowNormal"]);
        var plan = planner.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        if (opening == 0)
        {
            Assert.Empty(plan.Changes);
            Assert.Contains("at or above", Assert.Single(plan.SkippedRows).Reason);
        }
        else
        {
            Assert.Equal(opening, Assert.Single(plan.Changes, change => !change.Derived).NewValue.AsUInt32());
        }
        Assert.True(planner.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(opening == 0 ? staged : opening, document.GetValue(save.ArrowId).AsUInt32());
        Assert.Equal(document.GetValue(save.ArrowId), document.GetValue(save.ArrowId + "-copy"));
    }

    [Theory]
    [InlineData(0, 2, 2)]
    [InlineData(2, 2, 2)]
    [InlineData(1, 32768, 32768)]
    [InlineData(1, 2, 3)]
    public void IneligibleImportedAmmoAndNonAmmoRowsAreSkipped(uint flags, uint primary, uint copy)
    {
        var save = new SyntheticSave(yotei: false);
        save.Write(save.Inventory["ArrowNormal"], flags);
        SetAmmo(save, "ArrowNormal", primary);
        save.Write(save.Inventory["ArrowNormal"] + 2 * sizeof(uint), copy);
        save.Repair();
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(ExcludedRows);
        var plan = planner.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(ExcludedRows, plan.SkippedRows.Select(row => row.RowKey));
        Assert.True(planner.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(save.Bytes, Bytes(document));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void EligibleFlagsRestoreAndReimportEstablishesANewBaseline(uint flags)
    {
        var save = new SyntheticSave(yotei: false);
        save.Write(save.Inventory["ArrowNormal"], flags);
        save.Repair();
        using var document = save.Open();
        Assert.True(document.ApplyEdits([new(save.ArrowId, FieldValue.Of(0L))]).Succeeded);
        using var reimported = SuckerPunchTestData.Open(SuckerPunchTestData.WithBytes(Bytes(document)));
        var scope = new ActionScope(["ArrowNormal"]);
        Assert.Empty(((IActionPlanningDocument)reimported).PlanAction(ActionId, NoParameters, scope).Changes);
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(ActionId, NoParameters, scope);
        Assert.True(planner.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(save.Bytes, Bytes(document));
    }

    [Fact]
    public void EmptyInvalidAndForgedRequestsCannotWidenSelectionOrMutate()
    {
        var save = new SyntheticSave(yotei: false);
        using var document = save.Open();
        Assert.True(document.ApplyEdits([new(save.ArrowId, FieldValue.Of(0L))]).Succeeded);
        var staged = Bytes(document);
        var planner = (IActionPlanningDocument)document;
        Assert.Empty(planner.PlanAction(ActionId, NoParameters, new([])).Changes);
        ActionScope[] invalid = [new(["unknown"]), new() { QueryScope = new("inventory", new(), 0) }];
        foreach (var scope in invalid)
        {
            var rejected = planner.PlanAction(ActionId, NoParameters, scope);
            Assert.NotEmpty(rejected.Errors);
            Assert.False(planner.ApplyActionPlan(ActionId, NoParameters, scope, rejected).Succeeded);
        }
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(7L) }, new()).Errors);
        var plan = planner.PlanAction(ActionId, NoParameters, new());
        var first = plan.Changes[0];
        ActionPlan[] forged = [plan with { Changes = [] }, plan with { Warnings = [] },
            plan with { SkippedRows = [new("fake", "fake")] }, plan with { Errors = [new(ActionId, "fake")] },
            plan with { Changes = [first with { NewValue = FieldValue.Of(32767L) }, .. plan.Changes.Skip(1)] }];
        Assert.All(forged, preview => Assert.False(planner.ApplyActionPlan(ActionId, NoParameters, new(), preview).Succeeded));
        Assert.Equal(staged, Bytes(document));
        Assert.True(document.ApplyEdits([new(save.ArrowId, FieldValue.Of(1L))]).Succeeded);
        var changed = Bytes(document);
        Assert.False(planner.ApplyActionPlan(ActionId, NoParameters, new(), plan).Succeeded);
        Assert.Equal(changed, Bytes(document));
    }

    [Fact]
    public void SelectedRowsStayBoundedAndHigherAmmoAndResourcesStayUnchanged()
    {
        var save = new SyntheticSave(yotei: false);
        using var document = save.Open();
        Assert.True(document.ApplyEdits([new(save.ArrowId, FieldValue.Of(0L)), new(FireId, FieldValue.Of(3L)),
            new(save.ResourceId, FieldValue.Of(0L))]).Succeeded);
        var planner = (IActionPlanningDocument)document;
        var higher = planner.PlanAction(ActionId, NoParameters, new(["ArrowFire"]));
        Assert.Empty(higher.Errors);
        Assert.Empty(higher.Changes);
        Assert.Equal("ArrowFire", Assert.Single(higher.SkippedRows).RowKey);
        Assert.True(planner.ApplyActionPlan(ActionId, NoParameters, new(["ArrowFire"]), higher).Succeeded);
        Assert.Equal(0L, document.GetValue(save.ArrowId).AsInt64());
        var scope = new ActionScope(["ArrowNormal", "TestItem"]);
        var plan = planner.PlanAction(ActionId, NoParameters, scope);
        Assert.Equal(save.ArrowId, Assert.Single(plan.Changes, change => !change.Derived).ValueId);
        Assert.Equal("TestItem", Assert.Single(plan.SkippedRows).RowKey);
        Assert.True(planner.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(2L, document.GetValue(save.ArrowId).AsInt64());
        Assert.Equal(3L, document.GetValue(FireId).AsInt64());
        Assert.Equal(0L, document.GetValue(save.ResourceId).AsInt64());
        Assert.Empty(planner.PlanAction(ActionId, NoParameters, new()).Changes);
    }

    [Theory]
    [InlineData(256, 1)] // Equal byte sums need no checksum change even though both count words change.
    [InlineData(511, 256)]
    public void RestoreChecksumMatchesIndependentByteSumAcrossCarries(uint opening, uint staged)
    {
        var save = new SyntheticSave(yotei: false);
        SetAmmo(save, "ArrowNormal", opening);
        save.Repair();
        using var document = save.Open();
        Assert.True(document.ApplyEdits([new(save.ArrowId, FieldValue.Of((ulong)staged))]).Succeeded);
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(["ArrowNormal"]);
        var plan = planner.PlanAction(ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(opening == 256 ? 2 : 3, plan.Changes.Count);
        Assert.True(planner.ApplyActionPlan(ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(save.Bytes, Bytes(document));
        Assert.Equal(SuckerPunchTestData.Checksum(Bytes(document)), SuckerPunchTestData.Word(Bytes(document), save.ChecksumOffset));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrecognizedInventoryAndOtherTitleCannotUseAmmoRestoration(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        if (!yotei)
        {
            save.Bytes[save.AnchorStart + sizeof(uint)] = (byte)'X';
            save.Repair();
        }
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(ActionId, NoParameters, new());
        Assert.NotEmpty(plan.Errors);
        Assert.False(planner.ApplyActionPlan(ActionId, NoParameters, new(), plan).Succeeded);
        Assert.Equal(save.Bytes, Bytes(document));
    }

    [Theory]
    [InlineData(46)]
    [InlineData(50)]
    public void UnobservedVersionsRejectWithoutChanges(uint version)
    {
        var save = new SyntheticSave(yotei: false);
        save.Write(12, version);
        save.Repair();
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(ActionId, NoParameters, new());
        Assert.NotEmpty(plan.Errors);
        Assert.False(planner.ApplyActionPlan(ActionId, NoParameters, new(), plan).Succeeded);
        Assert.Equal(save.Bytes, Bytes(document));
    }

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();

    private static void SetAmmo(SyntheticSave save, string name, uint count)
    {
        save.Write(save.Inventory[name] + sizeof(uint), count);
        save.Write(save.Inventory[name] + 2 * sizeof(uint), count);
    }
}
