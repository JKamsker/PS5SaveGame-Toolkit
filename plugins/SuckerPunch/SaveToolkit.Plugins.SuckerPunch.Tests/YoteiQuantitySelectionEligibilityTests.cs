using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.SuckerPunch.Tests.QuantityPlanningTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiQuantitySelectionEligibilityTests
{
    private const string ActionId = "yotei/inventory/set-quantity";
    private const string Prefix = "yotei/inventory/";
    // Independent transcription of the documented native writer scope and conservative bounds.
    private static readonly IReadOnlyDictionary<string, uint> Bounds = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        ["ArrowNormal"] = 9, ["Mon"] = 112_099, ["Flowers"] = 22, ["IngredComWood"] = 95_099,
        ["IngredComCloth"] = 52, ["IngredComMetal"] = 13, ["IngredRarMetal"] = 10,
        ["IngredUncPowder"] = 50, ["IngredComFood"] = 1, ["IngredComPlant"] = 3,
        ["ArrowFire"] = 3, ["ArrowHeavy"] = 8, ["ArrowDisarm"] = 4, ["AmmoSake"] = 1,
    };

    [Fact]
    public void EveryExistingEligibleTypeStillPlansThroughTheBoundControlWithExactCopiesAndIntegrityWarning()
    {
        var save = CreateSave();
        using var document = save.Open();
        var tab = QuantitySelectionBindingTests.InventoryTab(document.BuildView());
        var table = Assert.Single(tab.Children.OfType<TableNode>());
        var action = Assert.Single(tab.Children.OfType<ActionNode>());
        Assert.Equal(ActionId, action.ActionId);
        Assert.Equal("yotei/inventory", table.SelectionId);
        Assert.Equal(table.SelectionId, action.SelectionTableId);
        Assert.Equal(14, table.Rows.Count(row => !Assert.IsType<FieldNode>(row.Cells["quantity"]).Spec.ReadOnly));
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(Bounds.Keys.Append("TestItem").ToArray());
        var plan = planner.PlanAction(action.ActionId, Target(0), scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(Bounds.Keys.Select(name => Prefix + name + "/quantity").Order(StringComparer.Ordinal),
            plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId).Order(StringComparer.Ordinal));
        Assert.Equal("TestItem", Assert.Single(plan.SkippedRows).RowKey);
        Assert.Contains("Checksum coverage beyond the declared end is unknown", Assert.Single(plan.Warnings));
        Assert.Contains("game may reject", action.Description);
        Assert.Equal(save.Bytes, Bytes(document));
        Assert.True(planner.ApplyActionPlan(action.ActionId, Target(0), scope, plan).Succeeded);
        var allowed = Bounds.Keys.Select(name => (save.Inventory[name] + save.QuantityRelativeOffset,
            Ammo(name) ? 2 * sizeof(uint) : sizeof(uint))).Append((save.ChecksumOffset, sizeof(uint))).ToArray();
        WriteAssertions.OnlyRangesChanged(save.Bytes, Bytes(document), allowed);
        Assert.Equal(YoteiTestData.Checksum(Bytes(document)), SuckerPunchTestData.Word(Bytes(document), save.ChecksumOffset));
        using var reopened = YoteiTestData.Open(YoteiTestData.WithBytes(Bytes(document)));
        foreach (var name in Bounds.Keys)
        {
            Assert.Equal(0U, reopened.GetValue(Prefix + name + "/quantity").AsUInt32());
            if (Ammo(name))
            {
                Assert.Contains(plan.Changes, change => change.ValueId == Prefix + name + "/quantity-copy" && change.Derived);
                Assert.Equal(0U, reopened.GetValue(Prefix + name + "/quantity-copy").AsUInt32());
            }
        }
    }

    [Fact]
    public void EachSelectedTypeRetainsItsOwnObservedBoundWithoutMutatingTheDocument()
    {
        foreach (var (name, max) in Bounds)
        {
            var save = CreateSave(openingQuantity: 0);
            using var document = save.Open();
            var planner = (IActionPlanningDocument)document;
            var scope = new ActionScope([name]);
            var atBound = planner.PlanAction(ActionId, Target(max), scope);
            Assert.Empty(atBound.Errors);
            Assert.Empty(atBound.SkippedRows);
            Assert.Equal(Prefix + name + "/quantity", Assert.Single(atBound.Changes, change => !change.Derived).ValueId);
            var aboveBound = planner.PlanAction(ActionId, Target((long)max + 1), scope);
            Assert.Empty(aboveBound.Changes);
            if (name == "Mon") Assert.NotEmpty(aboveBound.Errors);
            else Assert.Equal(name, Assert.Single(aboveBound.SkippedRows).RowKey);
            Assert.Equal(save.Bytes, Bytes(document));
        }
    }

    [Theory]
    [InlineData("flag")]
    [InlineData("eligibility byte")]
    [InlineData("ammo copy")]
    [InlineData("checksum")]
    public void BindingDoesNotEnableLockedInconsistentOrChecksumMismatchedRows(string invalid)
    {
        var save = CreateSave();
        var data = save.Inventory["ArrowNormal"];
        switch (invalid)
        {
            case "flag": save.Write(data, 0); break;
            case "eligibility byte": save.Bytes[data + sizeof(uint)] = 0; break;
            case "ammo copy": save.Write(data + save.QuantityRelativeOffset + sizeof(uint), 3); break;
        }
        save.Repair();
        if (invalid == "checksum") save.Write(save.ChecksumOffset, save.Checksum() + 1);
        using var document = save.Open();
        var tab = QuantitySelectionBindingTests.InventoryTab(document.BuildView());
        var field = Assert.IsType<FieldNode>(Assert.Single(tab.Children.OfType<TableNode>()).Rows
            .Single(row => row.Key == "ArrowNormal").Cells["quantity"]);
        Assert.True(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(["ArrowNormal"]);
        var plan = planner.PlanAction(ActionId, Target(0), scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal("ArrowNormal", Assert.Single(plan.SkippedRows).RowKey);
        Assert.True(planner.ApplyActionPlan(ActionId, Target(0), scope, plan).Succeeded);
        Assert.Equal(save.Bytes, Bytes(document));
    }

    [Fact]
    public void EmptyYoteiPlaceholderDeclaresNeitherInventorySelectionNorQuantityControl()
    {
        using var document = YoteiTestData.Open(YoteiTestData.WithBytes(new byte[616_448]));
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        Assert.DoesNotContain(tabs.Tabs, tab => tab.Title == "Inventory");
        Assert.False(document.CanEdit);
        Assert.NotEmpty(((IActionPlanningDocument)document).PlanAction(ActionId, Target(0), new()).Errors);
        Assert.Equal(PluginMaturity.Experimental, new SuckerPunchPlugin().Info.Maturity);
    }

    private static SyntheticSave CreateSave(uint openingQuantity = 1)
    {
        string[] names = ["ArrowNormal", .. Bounds.Keys.Where(name => name != "ArrowNormal"), "TestItem",
            .. Enumerable.Range(0, 32).Select(index => $"Unknown{index:D2}"), "SurvivalCurrency"];
        var save = new SyntheticSave(yotei: true, inventoryNames: names);
        foreach (var name in Bounds.Keys)
        {
            save.Write(save.Inventory[name] + save.QuantityRelativeOffset, openingQuantity);
            if (Ammo(name)) save.Write(save.Inventory[name] + save.QuantityRelativeOffset + sizeof(uint), openingQuantity);
        }
        save.Repair();
        return save;
    }

    private static bool Ammo(string name) => name.StartsWith("Arrow", StringComparison.Ordinal) || name.StartsWith("Ammo", StringComparison.Ordinal);
    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
}
