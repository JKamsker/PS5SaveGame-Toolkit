using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroSelectedWriteTests
{
    private const string ActionId = "collectibles/collect-all";

    [Theory]
    [InlineData("Artifact", 0)]
    [InlineData("Artifact", 1)]
    [InlineData("Puzzle", 0)]
    [InlineData("Puzzle", 1)]
    [InlineData("Prop", 0)]
    [InlineData("Prop", 1)]
    public void SelectedDuplicateNameChangesOnlyItsOccurrenceAndRestoresOriginalBytes(string group, int occurrence)
    {
        var original = AstroTestData.Bytes(Text(group));
        using var document = AstroTestData.Open(original);
        var fields = AstroTestData.Fields(document).Where(field => field.ValueId.StartsWith('/')).ToArray();
        var selected = $"/PreInstall[0]/{group}[0]/same[{occurrence}]";
        var valueId = selected + "/IsGot[0]/text[0]";
        var owned = $"/PreInstall[0]/{group}[0]/owned[0]";
        var scope = new ActionScope([selected, owned]);
        var parameters = new Dictionary<string, FieldValue>();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(valueId, change.ValueId);
        Assert.Equal(FieldValue.Of(false), change.OldValue);
        Assert.Equal(FieldValue.Of(true), change.NewValue);
        Assert.Equal(owned, Assert.Single(plan.SkippedRows).RowKey);
        Assert.Equal(original, AstroTestData.Output(document));

        var outcome = planner.ApplyActionPlan(ActionId, parameters, scope, plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(new ValueChange(valueId, FieldValue.Of(false), FieldValue.Of(true)), Assert.Single(outcome.Changes));
        var output = AstroTestData.Output(document);
        Assert.Equal(AstroTestData.Bytes(Text(group, occurrence)), output);
        using var reopened = AstroTestData.Open(output);
        Assert.All(fields, field => Assert.Equal(field.ValueId == valueId ? FieldValue.Of(true) : field.Value,
            reopened.GetValue(field.ValueId)));

        var restored = document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray());
        Assert.True(restored.Succeeded);
        Assert.Equal(original, AstroTestData.Output(document));
    }

    private static string Text(string group, int? ownedOccurrence = null) =>
        "\r\n<PreInstall><Common><NumCoin>0007</NumCoin></Common>" + $"<{group}>" +
        $"<same marker='first'><IsGot> \t{(ownedOccurrence == 0 ? "true" : "false")}\r\n </IsGot>" +
        "<IsCinematicsPlayed>false</IsCinematicsPlayed></same>" +
        $"<same marker='second'><IsGot>{(ownedOccurrence == 1 ? "true" : "false")}</IsGot>" +
        "<IsCinematicsPlayed>false</IsCinematicsPlayed></same>" +
        "<unselected><IsGot>false</IsGot></unselected><owned><IsGot>true</IsGot></owned>" +
        $"</{group}><Unknown a='中'><!--keep-->literal &amp; text</Unknown></PreInstall>opaque-残存<broken";
}
