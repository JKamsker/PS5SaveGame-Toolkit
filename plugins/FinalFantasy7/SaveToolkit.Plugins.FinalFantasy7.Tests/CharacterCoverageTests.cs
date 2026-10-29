using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class CharacterCoverageTests
{
    [Theory]
    [InlineData("PPSA01911", "ff7remake000", 0)]
    [InlineData("PPSA01911", "ff7remakeplus000", 5)]
    [InlineData("PPSA08664", "ff7rebirth000", 0)]
    [InlineData("PPSA18936", "ff7rebirth000", 0)]
    public void RecoveryPreviewsOnlySelectedVitalsAndAppliesAsOneReversibleEdit(string title, string name, int character)
    {
        using var fixture = new CoverageFixture(title, name, (bytes, schema) =>
        {
            var at = CharacterLayout.Offset(schema, character);
            CoverageFixture.Write(bytes, at + CharacterLayout.HpDelta, 0);
            CoverageFixture.Write(bytes, at + CharacterLayout.MpDelta, 1);
        });
        var preview = fixture.Preview(CoverageFixture.RecoveryAction, character);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(new[] { $"character/{character}/hp", $"character/{character}/mp" }, preview.Plan.Changes.Select(change => change.ValueId));
        Assert.All(preview.Plan.Changes, change =>
        {
            Assert.False(change.Derived);
            Assert.True(change.NewValue.AsInteger() > change.OldValue.AsInteger());
            var maximumId = change.ValueId.Replace("/hp", "/max-hp", StringComparison.Ordinal).Replace("/mp", "/max-mp", StringComparison.Ordinal);
            Assert.Equal(fixture.Document.GetValue(maximumId), change.NewValue);
        });
        Assert.Contains(preview.Plan.Warnings, warning => warning.StartsWith(WriteWarnings.Experimental, StringComparison.Ordinal));
        // Cancel is dismissing a pure preview: a replacement empty selection invalidates the abandoned token.
        var canceled = preview;
        var empty = fixture.Session.PlanAction(CoverageFixture.RecoveryAction, new Dictionary<string, System.Text.Json.JsonElement>(), new([]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(empty.Plan.Changes);
        Assert.True(fixture.Session.ApplyActionPlan(canceled.PlanId!, canceled.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        Assert.Equal(fixture.Original, fixture.Bytes());
        preview = fixture.Preview(CoverageFixture.RecoveryAction, character);
        var offset = CharacterLayout.Offset(fixture.Schema, character);
        fixture.ApplyAndVerifyHistory(preview, Enumerable.Range(offset + CharacterLayout.HpDelta, sizeof(uint))
            .Concat(Enumerable.Range(offset + CharacterLayout.MpDelta, sizeof(uint))));
    }

    [Theory]
    [InlineData("PPSA01911", "ff7remake000", 0)]
    [InlineData("PPSA01911", "ff7remakeplus000", 5)]
    [InlineData("PPSA08664", "ff7rebirth000", 0)]
    [InlineData("PPSA18936", "ff7rebirth000", 0)]
    public void LevelPreviewIncludesDerivedXpAndRestoresOriginalInconsistentTuple(string title, string name, int character)
    {
        using var fixture = new CoverageFixture(title, name, (bytes, schema) =>
        {
            var at = CharacterLayout.Offset(schema, character);
            bytes[at] = 40;
            CoverageFixture.Write(bytes, at + CharacterLayout.ExperienceDelta, 0);
        });
        var preview = fixture.Preview(CoverageFixture.LevelAction, character, 25);
        Assert.Empty(preview.Plan.Errors);
        var level = Assert.Single(preview.Plan.Changes, change => !change.Derived);
        Assert.Equal($"character/{character}/level", level.ValueId);
        Assert.Equal(25, level.NewValue.AsInt32());
        var xp = Assert.Single(preview.Plan.Changes, change => change.Derived);
        Assert.Equal($"character/{character}/experience", xp.ValueId);
        Assert.Equal(ProgressionEdits.CharacterCurves(fixture.Schema)[character][24], xp.NewValue.AsUInt32());
        Assert.Equal(fixture.Original, fixture.Bytes()); // Cancel without apply leaves the tuple intact.
        Assert.False(fixture.Session.State.CanUndo);
        var offset = CharacterLayout.Offset(fixture.Schema, character);
        fixture.ApplyAndVerifyHistory(preview, new[] { offset }.Concat(Enumerable.Range(offset + CharacterLayout.ExperienceDelta, sizeof(uint))));
    }

    [Theory]
    [InlineData(CoverageFixture.RecoveryAction)]
    [InlineData(CoverageFixture.LevelAction)]
    public void PlansSkipLockedUnknownAndNoncanonicalRowsAndRejectForeignQueries(string action)
    {
        using var fixture = new CoverageFixture("PPSA08664", "ff7rebirth000", (bytes, schema) =>
        {
            bytes[CharacterLayout.Offset(schema, 1)] = 0;
            bytes[CharacterLayout.Offset(schema, 2)] = byte.MaxValue;
            var offset = CharacterLayout.Offset(schema, 0);
            CoverageFixture.Write(bytes, offset + CharacterLayout.HpDelta, 1);
            CoverageFixture.Write(bytes, offset + CharacterLayout.MpDelta, 0);
        });
        IReadOnlyDictionary<string, FieldValue> parameters = action == CoverageFixture.LevelAction
            ? new Dictionary<string, FieldValue> { ["level"] = FieldValue.Of(25L) } : new Dictionary<string, FieldValue>();
        var scope = new ActionScope(["character/0", "character/1", "character/2", "character/7", "character/01", "inventory/0"]);
        var plan = fixture.Planner.PlanAction(action, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(5, plan.SkippedRows.Count);
        Assert.All(plan.Changes, change => Assert.StartsWith("character/0/", change.ValueId));
        Assert.NotEmpty(fixture.Planner.PlanAction(action, parameters, new() { QueryScope = new("inventory", new(), 0), RowKeys = ["character/0"] }).Errors);
        var tampered = plan with { Changes = [.. plan.Changes.Select(change => change with { Derived = !change.Derived })] };
        Assert.False(fixture.Planner.ApplyActionPlan(action, parameters, scope, tampered).Succeeded);
        Assert.False(fixture.Planner.ApplyActionPlan(action, parameters, scope, plan with { Warnings = [] }).Succeeded);
        Assert.False(fixture.Planner.ApplyActionPlan(action, parameters, scope, plan with { Errors = [new("fake", "invalid")] }).Succeeded);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    [Fact]
    public void RecoveryPreservesOverMaxAndFullVitalsAndNeverUsesTheCurrentValueAsATargetMaximum()
    {
        using var fixture = new CoverageFixture("PPSA08664", "ff7rebirth000", (bytes, schema) =>
        {
            var at = CharacterLayout.Offset(schema, 0) + CharacterLayout.HpDelta;
            CoverageFixture.Write(bytes, at, CoverageFixture.Read(bytes, at + CharacterLayout.MaximumVitalDelta) + 10);
        });
        var preview = fixture.Preview(CoverageFixture.RecoveryAction, 0);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Changes);
        Assert.Single(preview.Plan.SkippedRows);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    [Fact]
    public void LevelLimitsDuplicateZeroTiersAndSameLevelNoOpsAreExplicit()
    {
        using var fixture = new CoverageFixture("PPSA01911", "ff7remakeplus000");
        foreach (var value in new[] { FieldValue.Of(0L), FieldValue.Of(51L), FieldValue.Of(-1L), FieldValue.Of("25"), FieldValue.Of(true) })
            Assert.NotEmpty(fixture.Planner.PlanAction(CoverageFixture.LevelAction, new Dictionary<string, FieldValue> { ["level"] = value }, new()).Errors);
        Assert.NotEmpty(fixture.Planner.PlanAction(CoverageFixture.RecoveryAction, new Dictionary<string, FieldValue> { ["extra"] = FieldValue.Of(1L) }, new()).Errors);
        var ambiguous = fixture.Preview(CoverageFixture.LevelAction, 5, 1);
        Assert.Empty(ambiguous.Plan.Changes);
        Assert.Contains(ambiguous.Plan.SkippedRows, row => row.Reason.Contains("tier", StringComparison.Ordinal));
        var current = fixture.Document.GetValue("character/5/level").AsInt32();
        Assert.Empty(fixture.Preview(CoverageFixture.LevelAction, 5, current).Plan.Changes);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    [Theory]
    [InlineData(CoverageFixture.RecoveryAction)]
    [InlineData(CoverageFixture.LevelAction)]
    public void MultipleCharactersApplyInOneHistoryEntryAndDirectPlansRejectStaleValues(string action)
    {
        using var fixture = new CoverageFixture("PPSA08664", "ff7rebirth000", (bytes, schema) =>
        {
            for (var character = 0; character < 2; character++)
            {
                var at = CharacterLayout.Offset(schema, character);
                CoverageFixture.Write(bytes, at + CharacterLayout.HpDelta, 0);
                CoverageFixture.Write(bytes, at + CharacterLayout.MpDelta, 0);
            }
        });
        var parameters = action == CoverageFixture.LevelAction
            ? new Dictionary<string, System.Text.Json.JsonElement> { ["level"] = System.Text.Json.JsonSerializer.SerializeToElement(70) }
            : new Dictionary<string, System.Text.Json.JsonElement>();
        var scope = new ActionScope(["character/0", "character/1"]);
        var preview = fixture.Session.PlanAction(action, parameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(4, preview.Plan.Changes.Count);
        var offsets = Enumerable.Range(0, 2).SelectMany(character =>
        {
            var at = CharacterLayout.Offset(fixture.Schema, character);
            return action == CoverageFixture.LevelAction
                ? new[] { at }.Concat(Enumerable.Range(at + CharacterLayout.ExperienceDelta, sizeof(uint)))
                : Enumerable.Range(at + CharacterLayout.HpDelta, sizeof(uint)).Concat(Enumerable.Range(at + CharacterLayout.MpDelta, sizeof(uint)));
        });
        fixture.ApplyAndVerifyHistory(preview, offsets);
        IReadOnlyDictionary<string, FieldValue> directParameters = action == CoverageFixture.LevelAction
            ? new Dictionary<string, FieldValue> { ["level"] = FieldValue.Of(70L) } : new Dictionary<string, FieldValue>();
        var edited = fixture.Bytes();
        Assert.False(fixture.Planner.ApplyActionPlan(action, directParameters, scope, preview.Plan).Succeeded);
        Assert.Equal(edited, fixture.Bytes());
    }

    [Theory]
    [InlineData(CoverageFixture.RecoveryAction)]
    [InlineData(CoverageFixture.LevelAction)]
    public void DocumentScopeAndEmptySelectionStayDistinctWithoutBuildingTheView(string action)
    {
        using var fixture = new CoverageFixture("PPSA08664", "ff7rebirth000");
        using var document = fixture.Plugin.Open(fixture.File);
        var planner = (IActionPlanningDocument)document;
        IReadOnlyDictionary<string, FieldValue> parameters = action == CoverageFixture.LevelAction
            ? new Dictionary<string, FieldValue> { ["level"] = FieldValue.Of(1L) } : new Dictionary<string, FieldValue>();
        var plan = planner.PlanAction(action, parameters, new());
        Assert.Empty(plan.Errors);
        Assert.Equal(Ff7Schema.CharacterCount, plan.SkippedRows.Count + plan.Changes.Select(change => change.ValueId.Split('/')[1]).Distinct().Count());
        Assert.True(planner.ApplyActionPlan(action, parameters, new(), plan).Succeeded);
        var empty = planner.PlanAction(action, parameters, new([]));
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
    }
}
