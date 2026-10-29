using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroPlanCharacterizationTests
{
    private const string CollectAction = "collectibles/collect-all";
    private const string CoinAction = "common/set-coins";
    private const string Artifact = "/PreInstall[0]/Artifact[0]/";
    private const string Root = "\r\n<PreInstall><Common><NumCoin>0</NumCoin></Common><Artifact>" +
        "<zeta><IsGot>false</IsGot></zeta><alpha><IsGot>false</IsGot></alpha>" +
        "<owned><IsGot>true</IsGot></owned><unknown/><alsoOwned><IsGot>true</IsGot></alsoOwned>" +
        "</Artifact></PreInstall>opaque";
    private const string Experimental = "Experimental: not validated on console. Game limits and linked progress effects are unverified.";
    private const string Integrity = "Integrity is unverified: no game checksum/signature algorithm is known; the game may reject an edited save.";
    private const string Collection = "Inferred from existing IsGot boolean fields and mixed-event sample differences. Only IsGot is set to true; " +
        "cinematic, notification, Progress/World and UDS fields are preserved. Pickup dependencies and rewards are " +
        "unverified; no completion or trophy guarantee is made.";

    [Fact]
    public void CollectionPreviewPinsOrderedChangesSkipsAndExactWarnings()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(CollectAction, new Dictionary<string, FieldValue>(), new());
        Assert.Empty(plan.Errors);
        Assert.Equal(new[]
        {
            new PlannedChange(Artifact + "zeta[0]/IsGot[0]/text[0]", "Artifact/zeta IsGot (Inferred)", FieldValue.Of(false), FieldValue.Of(true)),
            new PlannedChange(Artifact + "alpha[0]/IsGot[0]/text[0]", "Artifact/alpha IsGot (Inferred)", FieldValue.Of(false), FieldValue.Of(true))
        }, plan.Changes);
        Assert.Equal(new[]
        {
            new SkippedActionRow(Artifact + "owned[0]", "Already owned; no change proposed."),
            new SkippedActionRow(Artifact + "unknown[0]", "No unique editable boolean IsGot field; unknown structure is preserved."),
            new SkippedActionRow(Artifact + "alsoOwned[0]", "Already owned; no change proposed.")
        }, plan.SkippedRows);
        Assert.Equal(new[] { Experimental, Collection, Integrity }, plan.Warnings);
        Assert.Equal(AstroTestData.Bytes(Root), AstroTestData.Output(document));
        var outcome = planner.ApplyActionPlan(CollectAction, new Dictionary<string, FieldValue>(), new(), plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(plan.Warnings, outcome.Warnings);
        Assert.Equal(2, outcome.Changes.Count);
    }

    [Theory]
    [InlineData("change-order")]
    [InlineData("label")]
    [InlineData("value-id")]
    [InlineData("old-value")]
    [InlineData("new-value")]
    [InlineData("derived")]
    [InlineData("old-exists")]
    [InlineData("new-exists")]
    [InlineData("skip-order")]
    [InlineData("skip-reason")]
    [InlineData("warning-order")]
    [InlineData("warning-case")]
    [InlineData("errors")]
    public void AnyChangedReviewedConsequenceRejectsWithExactStaleDiagnosticAndNoWarnings(string alteration)
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var plan = planner.PlanAction(CollectAction, parameters, new());
        var change = plan.Changes[0];
        var tampered = alteration switch
        {
            "change-order" => plan with { Changes = plan.Changes.Reverse().ToArray() },
            "label" => Replace(change with { Label = "Altered" }),
            "value-id" => Replace(change with { ValueId = "foreign" }),
            "old-value" => Replace(change with { OldValue = FieldValue.Of(true) }),
            "new-value" => Replace(change with { NewValue = FieldValue.Of(false) }),
            "derived" => Replace(change with { Derived = true }),
            "old-exists" => Replace(change with { OldExists = false }),
            "new-exists" => Replace(change with { NewExists = false }),
            "skip-order" => plan with { SkippedRows = plan.SkippedRows.Reverse().ToArray() },
            "skip-reason" => plan with { SkippedRows = [plan.SkippedRows[0] with { Reason = "Altered" }, .. plan.SkippedRows.Skip(1)] },
            "warning-order" => plan with { Warnings = plan.Warnings.Reverse().ToArray() },
            "warning-case" => plan with { Warnings = ["experimental: not validated on console. Game limits and linked progress effects are unverified.", .. plan.Warnings.Skip(1)] },
            "errors" => plan with { Errors = [new FieldError(CollectAction, "Altered")] },
            _ => throw new ArgumentOutOfRangeException(nameof(alteration))
        };
        var outcome = planner.ApplyActionPlan(CollectAction, parameters, new(), tampered);
        Assert.False(outcome.Succeeded);
        Assert.Equal(new FieldError(CollectAction, "The ASTRO preview is stale or no longer matches the document."), Assert.Single(outcome.Errors));
        Assert.Empty(outcome.Warnings);
        Assert.Empty(outcome.Changes);
        Assert.Equal(AstroTestData.Bytes(Root), AstroTestData.Output(document));

        ActionPlan Replace(PlannedChange replacement) => plan with { Changes = [replacement, .. plan.Changes.Skip(1)] };
    }

    [Fact]
    public void FreshValidationErrorsTakePrecedenceOverMismatchesAndDoNotCarryWarnings()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(CollectAction, new Dictionary<string, FieldValue>(), new());
        var invalid = new Dictionary<string, FieldValue> { ["unexpected"] = FieldValue.Of(true) };
        var outcome = planner.ApplyActionPlan(CollectAction, invalid, new(), plan);
        Assert.Equal(new FieldError(CollectAction, "Collect existing records takes no parameters."), Assert.Single(outcome.Errors));
        Assert.Empty(outcome.Warnings);
        Assert.Empty(outcome.Changes);
        var scope = new ActionScope { QueryScope = new("astro.collectibles", new QueryableCollectionQuery(), 0) };
        var unresolved = planner.ApplyActionPlan(CollectAction, new Dictionary<string, FieldValue>(), scope, plan);
        Assert.Equal(new FieldError(CollectAction, "Collectible query scopes must be resolved to row keys by Core before planning."), Assert.Single(unresolved.Errors));
        Assert.Empty(unresolved.Warnings);
        Assert.Equal(AstroTestData.Bytes(Root), AstroTestData.Output(document));
    }

    [Fact]
    public void EmptySelectionAndUnchangedCoinsApplyWithoutChangesButKeepPlanWarnings()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes(Root));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var scope = new ActionScope([]);
        var empty = planner.PlanAction(CollectAction, new Dictionary<string, FieldValue>(), scope);
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        Assert.Equal(new[] { Experimental, Collection, Integrity }, empty.Warnings);
        var collected = planner.ApplyActionPlan(CollectAction, new Dictionary<string, FieldValue>(), scope, empty);
        Assert.True(collected.Succeeded);
        Assert.Empty(collected.Changes);
        Assert.Equal(empty.Warnings, collected.Warnings);
        var parameters = new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(0L) };
        var coins = planner.PlanAction(CoinAction, parameters, new());
        Assert.Empty(coins.Changes);
        Assert.Equal(new[] { Experimental, Integrity,
            "Only the 0–1749 coin range is observed; 0–2,147,483,647 is a storage policy, not a proven gameplay range.",
            "Target already equals the saved coin value." }, coins.Warnings);
        var applied = planner.ApplyActionPlan(CoinAction, parameters, new(), coins);
        Assert.True(applied.Succeeded);
        Assert.Empty(applied.Changes);
        Assert.Equal(coins.Warnings, applied.Warnings);
        Assert.Equal(AstroTestData.Bytes(Root), AstroTestData.Output(document));
    }
}
