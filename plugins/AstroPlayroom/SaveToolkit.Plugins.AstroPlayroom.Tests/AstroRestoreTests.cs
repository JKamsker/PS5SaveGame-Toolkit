using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroRestoreTests
{
    private const string ActionId = "collectibles/restore-imported";
    private const string First = "/PreInstall[0]/Artifact[0]/same[0]";
    private const string Second = "/PreInstall[0]/Artifact[0]/same[1]";
    private const string Grant = "/PreInstall[0]/Artifact[0]/grant[0]";
    private const string Flag = "/IsGot[0]/text[0]";
    private const string Text = "\r\n<PreInstall><Artifact>" +
        "<same marker='first'><IsGot> true </IsGot><IsCinematicsPlayed>false</IsCinematicsPlayed></same>" +
        "<same marker='second'><IsGot>true</IsGot></same><grant><IsGot>false</IsGot></grant>" +
        "<missing/><numeric><IsGot>1</IsGot></numeric>" +
        "<duplicate><IsGot>true</IsGot><IsGot>true</IsGot></duplicate>" +
        "<attribute><IsGot value='true'/></attribute><nested><IsGot><Unknown>true</Unknown></IsGot></nested>" +
        "</Artifact><Unknown a='中'>keep</Unknown></PreInstall>opaque-残存<broken";

    [Fact]
    public void RestoreUsesOpeningValuesAndPreservesHigherFlagsAndUnknownStructures()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Text));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var initial = planner.PlanAction(ActionId, parameters, new());
        Assert.Empty(initial.Errors);
        Assert.Empty(initial.Changes);
        Assert.Equal(8, initial.SkippedRows.Count);
        Assert.Equal(AstroTestData.Bytes(Text), AstroTestData.Output(document));
        Assert.True(document.ApplyEdits([new(First + Flag, FieldValue.Of(false)),
            new(Second + Flag, FieldValue.Of(false)), new(Grant + Flag, FieldValue.Of(true))]).Succeeded);
        var beforePreview = AstroTestData.Output(document);
        var selected = new ActionScope([First, Grant]);
        var plan = planner.PlanAction(ActionId, parameters, selected);
        Assert.Empty(plan.Errors);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(First + Flag, change.ValueId);
        Assert.Contains("Artifact/same", change.Label, StringComparison.Ordinal);
        Assert.Contains("Inferred", change.Label, StringComparison.Ordinal);
        Assert.Equal(FieldValue.Of(false), change.OldValue);
        Assert.Equal(FieldValue.Of(true), change.NewValue);
        Assert.False(change.Derived);
        Assert.Equal(Grant, Assert.Single(plan.SkippedRows).RowKey);
        Assert.Contains("above", plan.SkippedRows[0].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Warnings, warning => warning.Contains("not validated on console", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, warning => warning.Contains("reward", StringComparison.Ordinal));
        // Cancel is discarding this pure preview; a second preview has identical content.
        Assert.Equal(beforePreview, AstroTestData.Output(document));
        Assert.True(ActionPlans.ContentEquals(plan, planner.PlanAction(ActionId, parameters, selected)));
        var all = planner.PlanAction(ActionId, parameters, new());
        Assert.Equal(2, all.Changes.Count);
        Assert.Equal(6, all.SkippedRows.Count);
        Assert.Equal(5, all.SkippedRows.Count(row => row.Reason.Contains("unknown structure", StringComparison.Ordinal)));
        var applied = planner.ApplyActionPlan(ActionId, parameters, selected, plan);
        Assert.True(applied.Succeeded);
        Assert.Equal(plan.Warnings, applied.Warnings);
        Assert.Equal(new ValueChange(change.ValueId, change.OldValue, change.NewValue), Assert.Single(applied.Changes));
        var expected = Text.Replace("<same marker='second'><IsGot>true", "<same marker='second'><IsGot>false", StringComparison.Ordinal)
            .Replace("<grant><IsGot>false", "<grant><IsGot>true", StringComparison.Ordinal);
        Assert.Equal(AstroTestData.Bytes(expected), AstroTestData.Output(document));
        // Reimport establishes a new baseline; the still-reduced second occurrence stays false.
        using var reopened = AstroTestData.Open(AstroTestData.Output(document));
        Assert.Empty(Assert.IsAssignableFrom<IActionPlanningDocument>(reopened).PlanAction(ActionId, parameters, new()).Changes);
        Assert.Equal(Second + Flag, Assert.Single(planner.PlanAction(ActionId, parameters, new()).Changes).ValueId);
    }

    [Fact]
    public void InvalidRequestsAndTamperedOrStalePlansNeverApplyPartialChanges()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Text));
        Assert.True(document.ApplyEdits([new(First + Flag, FieldValue.Of(false)), new(Second + Flag, FieldValue.Of(false))]).Succeeded);
        var before = AstroTestData.Output(document);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var empty = planner.PlanAction(ActionId, parameters, new([]));
        Assert.Empty(empty.Errors);
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        Assert.True(planner.ApplyActionPlan(ActionId, parameters, new([]), empty).Succeeded);
        foreach (var scope in new ActionScope[]
        {
            new([First, "unknown"]), new([First, First]), new(["/PreInstall[0]/Unknown[0]"]),
            new() { QueryScope = new("astro.collectibles", new QueryableCollectionQuery(), 0) },
            new([First]) { QueryScope = new("foreign", new QueryableCollectionQuery(), 0) }
        })
        {
            var invalid = planner.PlanAction(ActionId, parameters, scope);
            Assert.NotEmpty(invalid.Errors);
            Assert.Empty(invalid.Changes);
            Assert.False(planner.ApplyActionPlan(ActionId, parameters, scope, invalid).Succeeded);
            Assert.Equal(before, AstroTestData.Output(document));
        }
        var unexpected = new Dictionary<string, FieldValue> { ["target"] = FieldValue.Of(false) };
        var invalidParameters = planner.PlanAction(ActionId, unexpected, new());
        Assert.NotEmpty(invalidParameters.Errors);
        Assert.False(planner.ApplyActionPlan(ActionId, unexpected, new(), invalidParameters).Succeeded);
        var plan = planner.PlanAction(ActionId, parameters, new());
        Assert.Equal(2, plan.Changes.Count);
        foreach (var tampered in new[]
        {
            plan with { Warnings = [] }, plan with { SkippedRows = [] },
            plan with { Changes = [plan.Changes[0] with { NewValue = FieldValue.Of(false) }, plan.Changes[1]] },
            plan with { Changes = [plan.Changes[0] with { Derived = true }, plan.Changes[1]] }
        })
        {
            Assert.False(planner.ApplyActionPlan(ActionId, parameters, new(), tampered).Succeeded);
            Assert.Equal(before, AstroTestData.Output(document));
        }
        Assert.True(document.ApplyEdits([new(First + Flag, FieldValue.Of(true))]).Succeeded);
        var changed = AstroTestData.Output(document);
        Assert.False(planner.ApplyActionPlan(ActionId, parameters, new(), plan).Succeeded);
        Assert.Equal(changed, AstroTestData.Output(document));
    }
}
