using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class ActionPlansMutationEffectivenessTests
{
    private static readonly FieldError Mismatch = new("action", "Review the changed plan.");

    [Theory]
    [InlineData("reviewed")]
    [InlineData("fresh")]
    public void ContentComparisonRejectsNullPlans(string argument)
    {
        var plan = new ActionPlan();

        Assert.Throws<ArgumentNullException>(argument, () => ActionPlans.ContentEquals(
            argument == "reviewed" ? null! : plan, argument == "fresh" ? null! : plan));
    }

    [Theory]
    [InlineData("reviewed")]
    [InlineData("replan")]
    [InlineData("mismatchError")]
    public void RevalidationRejectsInvalidArgumentsBeforeCallingThePlanner(string argument)
    {
        var calls = 0;
        var plan = new ActionPlan();
        ActionPlan Replan() { calls++; return plan; }

        Assert.Throws<ArgumentNullException>(argument, () => ActionPlans.Revalidate(
            argument == "reviewed" ? null! : plan,
            argument == "replan" ? null! : Replan,
            argument == "mismatchError" ? null! : Mismatch));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("prepareBindings")]
    [InlineData("selectEdits")]
    [InlineData("applyEdits")]
    public void ScalarApplicationRejectsNullCallbacksBeforePlanningOrBinding(string argument)
    {
        var calls = new List<string>();
        var plan = new ActionPlan();
        ActionPlan Replan() { calls.Add("plan"); return plan; }
        void Prepare(ActionPlan _) => calls.Add("bind");
        IReadOnlyList<ValueEdit> Select(ActionPlan _) { calls.Add("select"); return []; }
        EditOutcome Apply(IReadOnlyList<ValueEdit> _) { calls.Add("apply"); return new(); }

        Assert.Throws<ArgumentNullException>(argument, () => ActionPlans.ApplyScalar(plan, Replan, Mismatch,
            argument == "prepareBindings" ? null! : Prepare,
            argument == "selectEdits" ? null! : Select,
            argument == "applyEdits" ? null! : Apply,
            ActionPlanWarningPolicy.Merge));
        Assert.Empty(calls);
    }

    [Fact]
    public void UnknownWarningPolicyIsRejectedBeforeAnyPluginCallback()
    {
        var plan = new ActionPlan();
        var calls = 0;

        Assert.Throws<ArgumentOutOfRangeException>("warningPolicy", () => ActionPlans.ApplyScalar(plan,
            () => { calls++; return plan; }, Mismatch, _ => calls++,
            _ => { calls++; return []; }, _ => { calls++; return new(); },
            (ActionPlanWarningPolicy)int.MaxValue));
        Assert.Equal(0, calls);
    }
}
