using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonReferenceActionTests
{
    private const string Settings = """
        {"version":136,"data":[{"group_name":"/video/display","options":[
        {"name":"FrameRateModes","type":"string_list","value":"\u0051uality","index":1,"default_index":0,"values":["Performance","Quality"]}]}]}
        """;

    [Theory]
    [InlineData("/video/display", "FrameRateModes", "Performance", "Quality", "cp2077-v136-display")]
    [InlineData("/gameplay/difficulty", "GameDifficulty", "VeryHard", "Easy", "cp2077-v136-difficulty")]
    public void ActionPickerShowsButRejectsCatalogIdsAbsentFromImportedChoices(
        string group, string option, string recorded, string absent, string catalog)
    {
        var source = $$"""
            {"version":136,"data":[{"group_name":"{{group}}","options":[
            {"name":"{{option}}","type":"string_list","value":"{{recorded}}","index":0,"values":["{{recorded}}"]}]}]}
            """;
        using var fixture = Open(source);
        var action = SetAction(fixture);
        Assert.Equal(catalog, Assert.IsType<ReferenceSpec>(Assert.Single(action.Parameters).Spec).CatalogId);
        var before = fixture.Bytes;
        var state = fixture.Session.State;
        var page = fixture.Session.SearchReferences("value", new() { Text = absent }, state.Revision, action.ActionId);
        var candidate = Assert.Single(page.Items);
        Assert.Equal(absent, candidate.RawId);
        Assert.NotNull(candidate.Dataset);
        Assert.False(candidate.Eligibility.Selectable);
        Assert.Contains(candidate.Eligibility.Reasons, reason => reason.Contains("imported option's recorded choices", StringComparison.Ordinal));
        Assert.True(fixture.Session.FindReference("value", recorded, state.Revision, action.ActionId).Eligibility.Selectable);
        foreach (var rejected in new[] { absent, recorded.ToLowerInvariant(), "Unknown" })
        {
            var preview = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.Of(rejected))),
                scope: null, expectedRevision: state.Revision, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Null(preview.PlanId);
            Assert.NotEmpty(preview.Plan.Errors);
            Assert.Equal(before, fixture.Bytes);
            Assert.Equal(state, fixture.Session.State);
        }
        Assert.Throws<KeyNotFoundException>(() => fixture.Session.SearchReferences("value", new()));
        Assert.Throws<KeyNotFoundException>(() => fixture.Session.SearchReferences("value", new(), actionId: "unpublished"));
    }

    [Fact]
    public void DifficultyPickerWritesExactIdAndUsesImportedChoiceOrderForDerivedIndex()
    {
        const string source = """
            {"version":136,"data":[{"group_name":"/gameplay/difficulty","options":[
            {"name":"GameDifficulty","type":"string_list","value":"Story","index":1,"values":["VeryHard","Story"]}]}]}
            """;
        using var fixture = Open(source);
        var action = SetAction(fixture);
        var choice = Assert.Single(fixture.Session.SearchReferences("value", new() { Text = "Very Hard" }, actionId: action.ActionId).Items);
        Assert.Equal("VeryHard", choice.RawId);
        Assert.Equal("Very Hard", choice.Label);
        Assert.True(choice.Eligibility.Selectable);
        var plan = JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.Of(choice.RawId)));
        Assert.Equal(JsonEditingTests.Text("VeryHard"), Assert.Single(plan.Changes, c => !c.Derived).NewValue);
        Assert.Equal(JsonEditingTests.Number("0"), Assert.Single(plan.Changes, c => c.Derived).NewValue);
    }

    [Fact]
    public void UnavailableImportedIdIsPreservedAndOneUndoRestoresItsExactBytes()
    {
        var source = "\uFEFF" + Settings.Replace("\\u0051uality", "Unavailable", StringComparison.Ordinal);
        using var fixture = Open(source);
        var action = SetAction(fixture);
        var parameter = Assert.Single(action.Parameters);
        Assert.Equal("Unavailable", parameter.Value.AsString());
        Assert.Equal(Confidence.Inferred, parameter.Confidence);
        Assert.Contains("game may reject", parameter.Warning);
        var unknown = fixture.Session.FindReference("value", "Unavailable", actionId: action.ActionId);
        Assert.Equal("Unavailable", unknown.RawId);
        Assert.Null(unknown.Label);
        Assert.False(unknown.Eligibility.Selectable);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.Of("Performance")));
        var review = fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(review.Complete);
        Assert.Equal(2, review.Changes.Count);
        Assert.DoesNotContain(review.Changes, c => c.ValueId.StartsWith("catalogue/", StringComparison.Ordinal));
    }

    [Fact]
    public void UnmappedRecordedChoiceKeepsFileDerivedEnumEvenWithPackPresent()
    {
        using var fixture = Open(Settings.Replace("Performance", "Unmapped", StringComparison.Ordinal));
        var action = SetAction(fixture);
        Assert.IsType<EnumSpec>(Assert.Single(action.Parameters).Spec);
        Assert.DoesNotContain(fixture.Fields(), f => f.ValueId.StartsWith("catalogue/", StringComparison.Ordinal));
        JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.Of("0")));
    }

    [Fact]
    public void BrowsingDiscardedPreviewAndNoOpKeepOriginalBytesAndHistory()
    {
        using var fixture = Open("\uFEFF" + Settings);
        var action = SetAction(fixture);
        var before = fixture.Bytes;
        var state = fixture.Session.State;
        _ = fixture.Session.SearchReferences("value", new() { Text = "Performance" }, state.Revision, action.ActionId);
        _ = fixture.Session.FindReference("value", "Quality", state.Revision, action.ActionId);
        var discarded = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.Of("Performance"))),
            scope: null, expectedRevision: state.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(discarded.PlanId);
        Assert.Equal(2, discarded.Plan.Changes.Count);
        Assert.Equal(before, fixture.Bytes);
        Assert.Equal(state, fixture.Session.State);
        Assert.Empty(fixture.Session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes);

        // Closing the dialog sends no apply. A subsequent preview replaces its retained token.
        var noOp = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.Of("Quality"))),
            scope: null, expectedRevision: state.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(noOp.PlanId);
        Assert.Empty(noOp.Plan.Errors);
        Assert.Empty(noOp.Plan.Changes);
        Assert.True(fixture.Session.ApplyActionPlan(noOp.PlanId, noOp.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(before, fixture.Bytes);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.False(fixture.Session.State.IsDirty);
        Assert.True(fixture.Session.ApplyActionPlan(discarded.PlanId, discarded.Revision, TestContext.Current.CancellationToken).Stale);
        Assert.Equal(before, fixture.Bytes);
    }

    [Fact]
    public void CanceledPlanningAndApplyPreserveBytesAndHistory()
    {
        using var fixture = Open("\uFEFF" + Settings);
        var action = SetAction(fixture);
        var before = fixture.Bytes;
        var state = fixture.Session.State;
        var parameters = JsonArrayEditingTests.Parameters(("value", FieldValue.Of("Performance")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Session.PlanAction(action.ActionId, parameters,
            scope: null, expectedRevision: state.Revision, cancellationToken: cancellation.Token));
        var preview = fixture.Session.PlanAction(action.ActionId, parameters, scope: null, expectedRevision: state.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellation.Token));
        Assert.Equal(before, fixture.Bytes);
        Assert.Equal(state, fixture.Session.State);
    }

    [Fact]
    public void ReferenceQueriesAndPreviewRejectRevisionFromBeforeSettingEdit()
    {
        using var fixture = Open("\uFEFF" + Settings);
        var action = SetAction(fixture);
        var parameters = JsonArrayEditingTests.Parameters(("value", FieldValue.Of("Performance")));
        var preview = fixture.Session.PlanAction(action.ActionId, parameters, scope: null, expectedRevision: fixture.Session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        var current = fixture.Session.PlanAction(action.ActionId, parameters, scope: null, expectedRevision: preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(current.PlanId);
        Assert.True(fixture.Session.ApplyActionPlan(current.PlanId, current.Revision, TestContext.Current.CancellationToken).Ok);
        var after = fixture.Bytes;
        Assert.Throws<StaleViewException>(() => fixture.Session.SearchReferences("value", new(), preview.Revision, action.ActionId));
        Assert.Throws<StaleViewException>(() => fixture.Session.FindReference("value", "Quality", preview.Revision, action.ActionId));
        Assert.True(fixture.Session.PlanAction(action.ActionId, parameters, scope: null, expectedRevision: preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, TestContext.Current.CancellationToken).Stale);
        Assert.Equal(after, fixture.Bytes);
        Assert.Equal("Quality", fixture.Document.GetValue(Assert.Single(fixture.Fields(),
            f => f.ValueId.StartsWith("catalogue/", StringComparison.Ordinal)).ValueId).AsString());
    }

    private static JsonSessionFixture Open(string source) => new(source, title: "PPSA04027", fileName: "settings.json", useReferences: true);

    private static ActionNode SetAction(JsonSessionFixture fixture) => JsonSemanticTestHelper.Actions(fixture)
        .Single(a => a.ActionId.EndsWith("/set", StringComparison.Ordinal));
}
