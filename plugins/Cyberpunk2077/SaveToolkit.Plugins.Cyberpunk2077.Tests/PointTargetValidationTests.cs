using System.Buffers.Binary;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class PointTargetValidationTests
{
    [Fact]
    public void InvalidTargetsAndScopesAndAlteredPlansAreAtomicEvenWithoutOpenedBindings()
    {
        var file = ProgressionSampleTests.Files()[0];
        using var document = new Cyberpunk2077Plugin().Open(file);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = PointTargetActionTests.TargetValues();
        foreach (var type in parameters.Keys)
        {
            foreach (var invalid in new[] { FieldValue.Of(-1L), FieldValue.Of(100_001L), FieldValue.Of("9"), FieldValue.Of(1.0) })
            {
                var invalidParameters = new Dictionary<string, FieldValue>(parameters) { [type] = invalid };
                var plan = planner.PlanAction(PointTargetActionTests.ActionId, invalidParameters, new());
                Assert.NotEmpty(plan.Errors);
                Assert.False(planner.ApplyActionPlan(PointTargetActionTests.ActionId, invalidParameters, new(), plan).Succeeded);
            }
            var missing = new Dictionary<string, FieldValue>(parameters);
            missing.Remove(type);
            Assert.NotEmpty(planner.PlanAction(PointTargetActionTests.ActionId, missing, new()).Errors);
        }
        var unknown = new Dictionary<string, FieldValue>(parameters) { ["Secondary"] = FieldValue.Of(2L) };
        Assert.NotEmpty(planner.PlanAction(PointTargetActionTests.ActionId, unknown, new()).Errors);
        foreach (var scope in new[] { new ActionScope(["Attribute"]), new ActionScope(["missing"]),
            new ActionScope { QueryScope = new("development", new(), 0) },
            new ActionScope(["Attribute"]) { QueryScope = new("inventory", new(), 0) } })
            Assert.NotEmpty(planner.PlanAction(PointTargetActionTests.ActionId, parameters, scope).Errors);
        var valid = planner.PlanAction(PointTargetActionTests.ActionId, parameters, new());
        Assert.Empty(valid.Errors);
        Assert.Equal(3, valid.Changes.Count);
        foreach (var altered in new[] { valid with { Changes = [] }, valid with { Warnings = [] }, valid with { SkippedRows = [] },
            valid with { Changes = [valid.Changes[0] with { NewValue = FieldValue.Of(100_001L) }, .. valid.Changes.Skip(1)] },
            valid with { Errors = [new("bad", "bad")] } })
            Assert.False(planner.ApplyActionPlan(PointTargetActionTests.ActionId, parameters, new(), altered).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        // Direct plugin callers also bind unopened targets before applying through the normal edit path.
        Assert.True(planner.ApplyActionPlan(PointTargetActionTests.ActionId, parameters, new(), valid).Succeeded);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(planner.ApplyActionPlan(PointTargetActionTests.ActionId, parameters, new(), valid).Succeeded);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EmptySelectionAndUnchangedTargetsAreNoOpsAndInterveningEditsInvalidatePreview()
    {
        var file = ProgressionSampleTests.Files()[0];
        var plugin = new Cyberpunk2077Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("point-stale", (DiskSaveFile)file, plugin, document);
        _ = session.BuildView();
        var empty = session.PlanAction(PointTargetActionTests.ActionId, PointTargetActionTests.Targets(12, 9, 3), new([]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(empty.PlanId);
        Assert.Empty(empty.Plan.Changes);
        Assert.True(session.ApplyActionPlan(empty.PlanId, empty.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.False(session.State.CanUndo);
        var unchanged = session.PlanAction(PointTargetActionTests.ActionId, PointTargetActionTests.Targets(7, 0, 0), new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(unchanged.PlanId);
        Assert.Empty(unchanged.Plan.Changes);
        Assert.Equal(4, unchanged.Plan.SkippedRows.Count);
        Assert.True(session.ApplyActionPlan(unchanged.PlanId, unchanged.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        var stale = session.PlanAction(PointTargetActionTests.ActionId, PointTargetActionTests.Targets(12, 9, 3), new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(stale.PlanId);
        Assert.True(session.ApplyEdits([new("progression/points/Attribute", System.Text.Json.JsonSerializer.SerializeToElement("8"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        Assert.False(session.ApplyActionPlan(stale.PlanId, stale.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(edited, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100_000)]
    public void ExistingPointBoundsAreAccepted(int target)
    {
        using var document = new Cyberpunk2077Plugin().Open(ProgressionSampleTests.Files()[0]);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = PointTargetActionTests.TargetValues().ToDictionary(p => p.Key, _ => FieldValue.Of(target));
        var plan = planner.PlanAction(PointTargetActionTests.ActionId, parameters, new());
        Assert.Empty(plan.Errors);
        Assert.True(planner.ApplyActionPlan(PointTargetActionTests.ActionId, parameters, new(), plan).Succeeded);
        using var reopened = ProgressionSampleTests.Open(Assert.Single(document.Serialize()).Content.ToArray());
        _ = reopened.BuildView();
        foreach (var type in parameters.Keys) Assert.Equal(FieldValue.Of(target), reopened.GetValue($"progression/points/{type}"));
    }

    [Fact]
    public void LargerImportedBalanceUsesItsOwnImmutableFieldLimit()
    {
        var bytes = PointTargetFixtures.RewritePoints((_, records) => records.Select(record =>
        {
            if (record.Enum("type", "gamedataDevelopmentPointType", "Attribute") != "Attribute") return record.Rebuild(new Dictionary<string, byte[]>());
            var value = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(value, 120_000);
            return record.Rebuild(new Dictionary<string, byte[]> { ["unspent"] = value });
        }));
        using var document = ProgressionSampleTests.Open(bytes);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = PointTargetActionTests.TargetValues();
        parameters["Attribute"] = FieldValue.Of(120_000L);
        var plan = planner.PlanAction(PointTargetActionTests.ActionId, parameters, new());
        Assert.Empty(plan.Errors);
        Assert.DoesNotContain(plan.Changes, c => c.ValueId.EndsWith("/Attribute", StringComparison.Ordinal));
        parameters["Primary"] = FieldValue.Of(120_000L);
        Assert.NotEmpty(planner.PlanAction(PointTargetActionTests.ActionId, parameters, new()).Errors);
        parameters["Primary"] = FieldValue.Of(9L);
        parameters["Attribute"] = FieldValue.Of(120_001L);
        Assert.NotEmpty(planner.PlanAction(PointTargetActionTests.ActionId, parameters, new()).Errors);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("progression/points/Attribute", FieldValue.Of(1L))]).Succeeded);
        _ = document.BuildView();
        parameters["Attribute"] = FieldValue.Of(120_000L);
        parameters["Primary"] = parameters["Espionage"] = FieldValue.Of(0L);
        var restoreOpening = planner.PlanAction(PointTargetActionTests.ActionId, parameters, new());
        Assert.Empty(restoreOpening.Errors);
        Assert.True(planner.ApplyActionPlan(PointTargetActionTests.ActionId, parameters, new(), restoreOpening).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
