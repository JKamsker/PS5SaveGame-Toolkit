using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ActionPlanCharacterizationTests
{
    private const string LinkedId = "slots/0/runes/linked";
    private const string RuneId = "slots/0/runes/set";
    private const string RefillId = "slots/0/inventory/refill";
    private const string ExperimentalWarning = "Experimental. Not validated on console. Based on the pinned ClayAmore "
        + "PlayStation layout and one PS5 snapshot. No PS per-slot checksum is known; any additional "
        + "integrity/dependency rules are unknown and the game may reject the edited save. "
        + "Source: ClayAmore / ER-Save-Editor (MIT), revision 014107f0, retrieved 2026-10-28: "
        + "https://github.com/ClayAmore/ER-Save-Editor/blob/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/save/common/save_slot.rs";

    [Theory]
    [InlineData("order")]
    [InlineData("id")]
    [InlineData("label")]
    [InlineData("old value")]
    [InlineData("new value")]
    [InlineData("derived")]
    [InlineData("old exists")]
    [InlineData("new exists")]
    [InlineData("warning order")]
    [InlineData("warning text")]
    [InlineData("skipped row")]
    [InlineData("error")]
    public void EveryReviewedConsequenceIsCheckedBeforeAnyWrite(string tampering)
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        var planner = (IActionPlanningDocument)document;
        var parameters = Parameters(1001);
        var plan = planner.PlanAction(LinkedId, parameters, new());
        Assert.Equal(2, plan.Changes.Count);
        Assert.Equal(2, plan.Warnings.Count);
        var first = plan.Changes[0];
        var changed = tampering switch
        {
            "id" => first with { ValueId = "slots/9/rune-memory" },
            "label" => first with { Label = "Changed label" },
            "old value" => first with { OldValue = FieldValue.Of(999L) },
            "new value" => first with { NewValue = FieldValue.Of(1002L) },
            "derived" => first with { Derived = false },
            "old exists" => first with { OldExists = false },
            "new exists" => first with { NewExists = false },
            _ => first,
        };
        var forged = tampering switch
        {
            "order" => plan with { Changes = plan.Changes.Reverse().ToArray() },
            "warning order" => plan with { Warnings = plan.Warnings.Reverse().ToArray() },
            "warning text" => plan with { Warnings = [plan.Warnings[0], "Changed warning"] },
            "skipped row" => plan with { SkippedRows = [new("invented", "Changed reason")] },
            "error" => plan with { Errors = [new("invented", "Changed error")] },
            _ => plan with { Changes = [changed, plan.Changes[1]] },
        };

        var outcome = planner.ApplyActionPlan(LinkedId, parameters, new(), forged);
        Assert.Equal([new FieldError(LinkedId, "The preview no longer matches the requested changes.")], outcome.Errors);
        Assert.Empty(outcome.Changes);
        Assert.Empty(outcome.Warnings);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Theory]
    [InlineData(RuneId)]
    [InlineData(LinkedId)]
    [InlineData(RefillId)]
    public void FreshValidationErrorsTakePrecedenceAndDoNotCopyPreviewWarnings(string actionId)
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        var planner = (IActionPlanningDocument)document;
        var invalid = Parameters(-1);
        var fresh = planner.PlanAction(actionId, invalid, new());
        var forged = new ActionPlan { Errors = [new("forged", "Forged error")], Warnings = ["Forged warning"] };

        var outcome = planner.ApplyActionPlan(actionId, invalid, new(), forged);
        Assert.NotEmpty(fresh.Errors);
        Assert.Equal(fresh.Errors, outcome.Errors);
        Assert.Empty(outcome.Warnings);
        Assert.Empty(outcome.Changes);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Theory]
    [InlineData(RuneId, 101L)]
    [InlineData(RuneId, 100L)]
    [InlineData(LinkedId, 1001L)]
    [InlineData(LinkedId, 100L)]
    [InlineData(RefillId, 0L)]
    public void PreviewTextAndApplicationWarningPolicyStayDistinctIncludingNoops(string actionId, long amount)
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        var planner = (IActionPlanningDocument)document;
        var parameters = actionId == RefillId ? new Dictionary<string, FieldValue>() : Parameters(amount);
        var plan = planner.PlanAction(actionId, parameters, new());
        var expectedWarnings = actionId switch
        {
            RuneId => new[] { ExperimentalWarning + " Only held runes change. "
                + "Lifetime history and its profile copy, equipment, vitals and regulation remain unchanged." },
            LinkedId => [ExperimentalWarning, "Lifetime-history meaning is Inferred; the game may reject this change. "
                + "Both known history copies are updated, without spending/death/recovery simulation."],
            _ => [ExperimentalWarning + " Legal carry/storage caps are unverified; only positive "
                + "existing ordinary stacks are editable, up to conservative observed ceilings (Rowa Fruit 12; Smithing Stone [1] 3)."],
        };
        Assert.Equal(expectedWarnings, plan.Warnings);
        if (actionId == LinkedId && amount == 1001)
            Assert.Equal(new[]
            {
                new PlannedChange("slots/0/rune-memory", "Lifetime history · live and menu copies",
                    FieldValue.Of(1000L), FieldValue.Of(1001L)) { Derived = true },
                new PlannedChange("slots/0/runes", "Runes held", FieldValue.Of(100L), FieldValue.Of(1001L)),
            }, plan.Changes);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);

        var applied = planner.ApplyActionPlan(actionId, parameters, new(), plan);
        Assert.True(applied.Succeeded);
        Assert.Empty(applied.Warnings); // Warnings belong to the preview, not the scalar edit outcome.
        Assert.Equal(plan.Changes.Select(change => change.ValueId), applied.Changes.Select(change => change.ValueId));
        if (applied.Changes.Count == 0) WriteTestSupport.EqualBytes(fixture.Bytes, document);
        else
        {
            Assert.True(document.ApplyEdits(applied.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
            WriteTestSupport.EqualBytes(fixture.Bytes, document);
        }
    }

    private static Dictionary<string, FieldValue> Parameters(long runes) => new() { ["runes"] = FieldValue.Of(runes) };
}
