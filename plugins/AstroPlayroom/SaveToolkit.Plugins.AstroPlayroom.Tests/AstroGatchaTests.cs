using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroGatchaTests
{
    private const string ActionId = "common/top-up-gatcha";

    [Fact]
    public void BudgetTopUpPreservesEveryOtherByteReopensAndNeverReducesOrRepeatedlyAwardsCoins()
    {
        foreach (var file in AstroTestData.Samples())
        {
            using var document = new AstroPlayroomPlugin().Open(file);
            var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
            var parameters = new Dictionary<string, FieldValue> { ["attempts"] = FieldValue.Of(17L) };
            var plan = planner.PlanAction(ActionId, parameters, new());
            Assert.Empty(plan.Errors);
            Assert.Equal(file.Directory.Generation == 0 ? 1 : 0, plan.Changes.Count);
            Assert.Contains(plan.Warnings, warning => warning.Contains("100 coins per attempt", StringComparison.Ordinal));
            Assert.Contains(plan.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
            Assert.True(planner.ApplyActionPlan(ActionId, parameters, new(), plan).Succeeded);
            var output = AstroTestData.Output(document);
            if (file.Directory.Generation == 0)
            {
                var original = file.ReadAllBytes();
                var end = original.AsSpan(AstroTestData.TextOffset).IndexOf((byte)0) + AstroTestData.TextOffset;
                var text = Encoding.UTF8.GetString(original, AstroTestData.TextOffset, end - AstroTestData.TextOffset);
                Assert.Equal(AstroTestData.Bytes(text.Replace("<NumCoin>0</NumCoin>", "<NumCoin>1700</NumCoin>", StringComparison.Ordinal)), output);
            }
            else Assert.Equal(file.ReadAllBytes(), output);
            using var reopened = AstroTestData.Open(output);
            Assert.Equal(file.Directory.Generation == 0 ? 1700 : 1749, AstroTestData.Find(reopened, AstroTestData.CoinsPath).Value.AsInt64());
            Assert.Empty(planner.PlanAction(ActionId, parameters, new()).Changes);
        }
    }

    [Fact]
    public void InvalidOrScopedBudgetsRejectAndZeroIsANoOp()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes());
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        foreach (var invalid in new[] { FieldValue.Of(-1L), FieldValue.Of(18L), FieldValue.Of(Int128.MaxValue), FieldValue.Of(true), FieldValue.Of("1") })
            Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["attempts"] = invalid }, new()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue>(), new()).Errors);
        var parameters = new Dictionary<string, FieldValue> { ["attempts"] = FieldValue.Of(1L) };
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new ActionScope([])).Errors);
        parameters["extra"] = FieldValue.Of(1L);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new()).Errors);
        Assert.Empty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["attempts"] = FieldValue.Of(0L) }, new()).Changes);
        Assert.Equal(AstroTestData.Bytes(), AstroTestData.Output(document));
    }

    [Fact]
    public void BudgetPreviewAndApplyUseOneCoreUndoRedoStep()
    {
        var file = AstroTestData.Samples()[0];
        var plugin = new AstroPlayroomPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("astro-budget", Assert.IsType<DiskSaveFile>(file), plugin, document);
        _ = session.BuildView();
        var preview = session.PlanAction(ActionId, new Dictionary<string, JsonElement> { ["attempts"] = JsonSerializer.SerializeToElement(1) }, scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(FieldValue.Of(100L), Assert.Single(preview.Plan.Changes).NewValue);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = AstroTestData.Output(document);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, AstroTestData.Output(document));
    }
}
