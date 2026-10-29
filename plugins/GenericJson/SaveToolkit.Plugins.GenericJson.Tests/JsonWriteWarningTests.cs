using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonWriteWarningTests
{
    private const string ConsoleRisk = "Not validated on console. The game may reject or overwrite the edited save.";
    private const string RawWarning = ConsoleRisk
        + " External integrity and game-specific dependencies are unknown. JSON validity does not establish game compatibility.";

    [Theory]
    [InlineData("null", "PPSA00000", "data.json")]
    [InlineData("[1,true,\"text\",null]", "PPSA00000", "data.json")]
    [InlineData("{\"unrecognized\":1}", "PPSA04027", "settings.json")]
    public void PlainAndUnrecognizedDocumentsWarnInOverviewAndEveryRawScalar(string source, string title, string fileName)
    {
        using var fixture = new JsonSessionFixture(source, title: title, fileName: fileName);
        var view = Assert.IsType<TabsNode>(fixture.Session.BuildView());
        var overview = view.Tabs.Single(tab => tab.Title == "Overview");
        Assert.Contains(overview.Children.OfType<TextNode>(), text => text.Text.StartsWith(RawWarning, StringComparison.Ordinal)
            && text.Tone == Tone.Warning);
        Assert.All(fixture.Fields(), field =>
        {
            Assert.Equal(Confidence.Unknown, field.Confidence);
            Assert.Equal(RawWarning, field.Warning);
        });
    }

    [Theory]
    [InlineData("[1]", "array/0/remove")]
    [InlineData("{\"a\":1}", "object/0/remove")]
    public void StructuralControlsPreviewAndOutcomeKeepSyntaxQualificationsAfterCommonWarning(string source, string actionId)
    {
        using var fixture = new JsonSessionFixture(source);
        var actions = JsonSemanticTestHelper.Actions(fixture).Where(action => action.ActionId.StartsWith("array/", StringComparison.Ordinal)
            || action.ActionId.StartsWith("object/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(actions);
        Assert.All(actions, action => Assert.StartsWith(RawWarning, action.Description, StringComparison.Ordinal));
        var preview = fixture.Session.PlanAction(actionId, JsonArrayEditingTests.Parameters(("at", FieldValue.Of(0L))), expectedRevision: 0, scope: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        var warning = Assert.Single(preview.Plan.Warnings);
        Assert.StartsWith(RawWarning, warning, StringComparison.Ordinal);
        Assert.Contains("Membership, names and order may have game-specific dependencies", warning, StringComparison.Ordinal);
        Assert.Contains("Templates describe JSON types, not a validated game schema.", warning, StringComparison.Ordinal);
        var outcome = fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(outcome.Ok);
        Assert.Contains(warning, outcome.Warnings);
    }

    [Fact]
    public void ResetControlPreviewAndOutcomeWarnAboutGameCompatibility()
    {
        using var fixture = new JsonSessionFixture("1");
        var field = Assert.Single(fixture.Fields());
        Assert.True(fixture.Edit(field.ValueId, JsonEditingTests.Number("2")).Ok);
        var action = JsonSemanticTestHelper.Actions(fixture).Single(action => action.ActionId == "reset-imported");
        Assert.StartsWith(RawWarning, action.Description, StringComparison.Ordinal);
        Assert.Contains("not game defaults", action.Description, StringComparison.Ordinal);
        var preview = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(), expectedRevision: fixture.Session.State.Revision, scope: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Contains(RawWarning, preview.Plan.Warnings);
        var outcome = fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(outcome.Ok);
        Assert.Contains(RawWarning, outcome.Warnings);
        Assert.Equal("1"u8.ToArray(), fixture.Bytes);
    }
}
