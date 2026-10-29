using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroGatchaBoundaryTests
{
    private const string ActionId = "common/top-up-gatcha";

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    [InlineData(2, 200)]
    [InlineData(3, 300)]
    [InlineData(4, 400)]
    [InlineData(5, 500)]
    [InlineData(6, 600)]
    [InlineData(7, 700)]
    [InlineData(8, 800)]
    [InlineData(9, 900)]
    [InlineData(10, 1000)]
    [InlineData(11, 1100)]
    [InlineData(12, 1200)]
    [InlineData(13, 1300)]
    [InlineData(14, 1400)]
    [InlineData(15, 1500)]
    [InlineData(16, 1600)]
    [InlineData(17, 1700)]
    public void EverySupportedBudgetTopsUpOnlyBelowTargetAndRepeatedApplyIsLossless(int attempts, int budget)
    {
        foreach (var balance in new[] { 0, Math.Max(0, budget - 1), budget, budget + 1, int.MaxValue }.Distinct())
        {
            // Preserve imported leading zeroes on no-ops; actual edits use the target spelling.
            var original = AstroTestData.Bytes(Text("000" + balance.ToString(CultureInfo.InvariantCulture)));
            using var document = AstroTestData.Open(original);
            var coins = AstroTestData.Find(document, AstroTestData.CoinsPath);
            var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
            var parameters = new Dictionary<string, FieldValue> { ["attempts"] = FieldValue.Of((long)attempts) };
            var plan = planner.PlanAction(ActionId, parameters, new());
            Assert.Empty(plan.Errors);
            Assert.Equal(original, AstroTestData.Output(document));

            var target = balance < budget ? budget : balance;
            var expectedChanges = target == balance ? Array.Empty<ValueChange>() :
                [new ValueChange(coins.ValueId, FieldValue.Of((long)balance), FieldValue.Of((long)target))];
            Assert.Equal(expectedChanges, plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)));
            var outcome = planner.ApplyActionPlan(ActionId, parameters, new(), plan);
            Assert.True(outcome.Succeeded);
            Assert.Equal(expectedChanges, outcome.Changes);
            Assert.Equal(plan.Warnings, outcome.Warnings);
            Assert.Equal(target != balance, outcome.StructureChanged);
            var expected = target == balance ? original : AstroTestData.Bytes(Text(target.ToString(CultureInfo.InvariantCulture)));
            Assert.Equal(expected, AstroTestData.Output(document));
            using var reopened = AstroTestData.Open(expected);
            Assert.Equal(FieldValue.Of((long)target), reopened.GetValue(coins.ValueId));

            var repeated = planner.PlanAction(ActionId, parameters, new());
            Assert.Empty(repeated.Errors);
            Assert.Empty(repeated.Changes);
            var repeatedOutcome = planner.ApplyActionPlan(ActionId, parameters, new(), repeated);
            Assert.True(repeatedOutcome.Succeeded);
            Assert.Empty(repeatedOutcome.Changes);
            Assert.False(repeatedOutcome.StructureChanged);
            Assert.Equal(expected, AstroTestData.Output(document));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("<NumCoin>true</NumCoin>")]
    [InlineData("<NumCoin>-1</NumCoin>")]
    [InlineData("<NumCoin>2147483648</NumCoin>")]
    [InlineData("<NumCoin>1</NumCoin><NumCoin>2</NumCoin>")]
    [InlineData("<NumCoin value='1'/>")]
    [InlineData("<NumCoin><Nested>1</Nested></NumCoin>")]
    [InlineData("<NumCoin>1<!--split-->2</NumCoin>")]
    public void MissingUnsupportedOrAmbiguousCoinSchemaRejectsPreviewAndApplyWithoutWrites(string coinMarkup)
    {
        var original = AstroTestData.Bytes($"\r\n<PreInstall><Common>{coinMarkup}</Common></PreInstall>opaque");
        using var document = AstroTestData.Open(original);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { ["attempts"] = FieldValue.Of(17L) };
        var plan = planner.PlanAction(ActionId, parameters, new());
        Assert.Equal(new FieldError(ActionId, "This schema has no unique editable Common/NumCoin integer."), Assert.Single(plan.Errors));
        Assert.Empty(plan.Changes);
        var outcome = planner.ApplyActionPlan(ActionId, parameters, new(), plan);
        Assert.False(outcome.Succeeded);
        Assert.Equal(plan.Errors, outcome.Errors);
        Assert.Empty(outcome.Changes);
        Assert.Equal(original, AstroTestData.Output(document));
    }

    private static string Text(string coins) =>
        $"\r\n<PreInstall><Common><NumCoin> \t{coins}\r\n </NumCoin><PlayCount>0002</PlayCount></Common>" +
        "<Artifact><unselected><IsGot>false</IsGot></unselected></Artifact>" +
        "<Unknown a='中'><!--keep-->literal &amp; text</Unknown></PreInstall>opaque-残存<broken";
}
