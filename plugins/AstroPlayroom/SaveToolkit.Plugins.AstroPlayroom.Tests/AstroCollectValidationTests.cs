using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroCollectValidationTests
{
    private const string CollectAction = "collectibles/collect-all";
    private const string FirstRow = "/PreInstall[0]/Artifact[0]/good[0]";
    private const string SecondRow = "/PreInstall[0]/Artifact[0]/other[0]";
    private const string Root = "\r\n<PreInstall><Artifact><good><IsGot>false</IsGot><IsCinematicsPlayed>false</IsCinematicsPlayed></good>" +
        "<other><IsGot>false</IsGot></other></Artifact><Unknown attr='kept'> untouched </Unknown></PreInstall>opaque";

    [Fact]
    public void EmptySelectionDoesNothingAndUnknownOrDuplicateKeysAndUnexpectedParametersAreRejected()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var emptyScope = new ActionScope([]);
        var empty = planner.PlanAction(CollectAction, new Dictionary<string, FieldValue>(), emptyScope);
        Assert.Empty(empty.Errors);
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        Assert.NotEmpty(empty.Warnings);
        Assert.True(planner.ApplyActionPlan(CollectAction, new Dictionary<string, FieldValue>(), emptyScope, empty).Succeeded);

        foreach (var keys in new[] { new[] { FirstRow, "unknown" }, new[] { FirstRow, FirstRow }, new[] { "/PreInstall[0]/Unknown[0]" } })
        {
            var scope = new ActionScope(keys);
            var invalid = planner.PlanAction(CollectAction, new Dictionary<string, FieldValue>(), scope);
            Assert.NotEmpty(invalid.Errors);
            Assert.Empty(invalid.Changes);
            Assert.False(planner.ApplyActionPlan(CollectAction, new Dictionary<string, FieldValue>(), scope, invalid).Succeeded);
        }

        Assert.NotEmpty(planner.PlanAction(CollectAction, new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(true) }, new()).Errors);
        var queryScope = new ActionScope { QueryScope = new ActionQueryScope("foreign", new QueryableCollectionQuery(), 0) };
        Assert.NotEmpty(planner.PlanAction(CollectAction, new Dictionary<string, FieldValue>(), queryScope).Errors);
        Assert.Equal(AstroTestData.Bytes(Root), AstroTestData.Output(document));
    }

    [Fact]
    public void InvalidOwnershipValuesRejectTheEntireBatchWithoutMutation()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var first = FirstRow + "/IsGot[0]/text[0]";
        var second = SecondRow + "/IsGot[0]/text[0]";
        foreach (var invalid in new[] { FieldValue.Of(1L), FieldValue.Of(-1L), FieldValue.Of("true"), FieldValue.Null })
        {
            var outcome = document.ApplyEdits([new ValueEdit(first, FieldValue.Of(true)), new ValueEdit(second, invalid)]);
            Assert.False(outcome.Succeeded);
            Assert.Empty(outcome.Changes);
            Assert.Equal(AstroTestData.Bytes(Root), AstroTestData.Output(document));
        }
    }

    [Fact]
    public void TamperedAndStalePlansAreRejectedWithoutPartialChanges()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var plan = planner.PlanAction(CollectAction, parameters, new());
        foreach (var tampered in new[]
        {
            plan with { Warnings = [] },
            plan with { Changes = [plan.Changes[0] with { NewValue = FieldValue.Of(false) }, plan.Changes[1]] },
            plan with { Changes = [plan.Changes[0] with { Derived = true }, plan.Changes[1]] },
            plan with { SkippedRows = [new SkippedActionRow(FirstRow, "hidden")] }
        })
        {
            Assert.False(planner.ApplyActionPlan(CollectAction, parameters, new(), tampered).Succeeded);
            Assert.Equal(AstroTestData.Bytes(Root), AstroTestData.Output(document));
        }

        Assert.True(document.ApplyEdits([new ValueEdit(plan.Changes[0].ValueId, FieldValue.Of(true))]).Succeeded);
        var edited = AstroTestData.Output(document);
        Assert.False(planner.ApplyActionPlan(CollectAction, parameters, new(), plan).Succeeded);
        Assert.Equal(edited, AstroTestData.Output(document));
    }

    [Fact]
    public void UnknownOrAmbiguousOwnershipStructuresAreSkippedWithoutInventingFields()
    {
        var text = "\r\n<PreInstall><Artifact>" +
            "<missing><Mystery>keep</Mystery></missing>" +
            "<numeric><IsGot>0</IsGot></numeric>" +
            "<duplicate><IsGot>false</IsGot><IsGot>false</IsGot></duplicate>" +
            "<attribute><IsGot value='false'/></attribute>" +
            "<nested><IsGot><Unknown>false</Unknown></IsGot></nested>" +
            "<owned><IsGot>true</IsGot></owned>" +
            "<good><IsGot>false</IsGot></good>" +
            "</Artifact><Common><ArmadilloUnlockPhase>3</ArmadilloUnlockPhase></Common></PreInstall>suffix";
        using var document = AstroTestData.Open(AstroTestData.Bytes(text));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var plan = planner.PlanAction(CollectAction, parameters, new());
        Assert.Single(plan.Changes);
        Assert.Equal(6, plan.SkippedRows.Count);
        Assert.Equal(5, plan.SkippedRows.Count(row => row.Reason.Contains("unknown structure", StringComparison.Ordinal)));
        Assert.True(planner.ApplyActionPlan(CollectAction, parameters, new(), plan).Succeeded);
        Assert.Equal(AstroTestData.Bytes(text.Replace("<good><IsGot>false", "<good><IsGot>true", StringComparison.Ordinal)), AstroTestData.Output(document));
    }
}
