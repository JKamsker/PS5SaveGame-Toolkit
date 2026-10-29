using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class ActionPlansTests
{
    private static readonly FieldError Mismatch = new("action", "Preview changed; review again.");
    private static ActionPlan Plan => new()
    {
        Changes = [new("a", "A", FieldValue.Of(ulong.MaxValue - 1), FieldValue.Of(ulong.MaxValue)),
            new("b", "B", FieldValue.Null, FieldValue.ExactNumber("1e99")) { Derived = true, OldExists = false }],
        SkippedRows = [new("locked/a", "Locked"), new("locked/b", "Unknown")],
        Warnings = ["Experimental", "Integrity unknown"],
    };

    [Fact]
    public void ContentComparisonUsesOrderedValues_NotListIdentity()
    {
        var original = Plan;
        Assert.True(ActionPlans.ContentEquals(original, Plan));
        Assert.True(ActionPlans.ContentEquals(original with { Errors = [new("x", "failure")] },
            Plan with { Errors = [new("x", "failure")] }));
    }

    [Theory]
    [InlineData("value")]
    [InlineData("precision")]
    [InlineData("label")]
    [InlineData("identity")]
    [InlineData("old-value")]
    [InlineData("derived")]
    [InlineData("old-exists")]
    [InlineData("new-exists")]
    [InlineData("changes-order")]
    [InlineData("skips-order")]
    [InlineData("skips-reason")]
    [InlineData("warnings-order")]
    [InlineData("warnings-text")]
    [InlineData("errors")]
    public void TamperedOrReorderedConsequencesAreRejectedBeforeBinding(string kind)
    {
        var plan = Plan;
        var first = plan.Changes[0];
        var changed = kind switch
        {
            "value" => plan with { Changes = [first with { NewValue = FieldValue.Of(1L) }, plan.Changes[1]] },
            "precision" => plan with { Changes = [first with { NewValue = FieldValue.Of(ulong.MaxValue - 1) }, plan.Changes[1]] },
            "label" => plan with { Changes = [first with { Label = "Other" }, plan.Changes[1]] },
            "identity" => plan with { Changes = [first with { ValueId = "other" }, plan.Changes[1]] },
            "old-value" => plan with { Changes = [first with { OldValue = FieldValue.Null }, plan.Changes[1]] },
            "derived" => plan with { Changes = [first with { Derived = true }, plan.Changes[1]] },
            "old-exists" => plan with { Changes = [first with { OldExists = false }, plan.Changes[1]] },
            "new-exists" => plan with { Changes = [first with { NewExists = false }, plan.Changes[1]] },
            "changes-order" => plan with { Changes = plan.Changes.Reverse().ToArray() },
            "skips-order" => plan with { SkippedRows = plan.SkippedRows.Reverse().ToArray() },
            "skips-reason" => plan with { SkippedRows = [new("locked/a", "Changed"), plan.SkippedRows[1]] },
            "warnings-order" => plan with { Warnings = plan.Warnings.Reverse().ToArray() },
            "warnings-text" => plan with { Warnings = ["experimental", "Integrity unknown"] },
            _ => plan with { Errors = [new("x", "failure")] },
        };
        Assert.False(ActionPlans.ContentEquals(changed, plan));
        var called = false;
        var outcome = ActionPlans.ApplyScalar(changed, () => plan, Mismatch, _ => called = true,
            _ => throw new InvalidOperationException("Must not select"), _ => throw new InvalidOperationException("Must not apply"),
            ActionPlanWarningPolicy.Merge);
        Assert.False(called);
        Assert.Equal(Mismatch, Assert.Single(outcome.Errors));
        Assert.Equal(plan.Warnings, outcome.Warnings);
    }

    [Fact]
    public void RevalidationReturnsFreshFailures_AndCallsPlannerExactlyOnce()
    {
        var fresh = Plan with { Errors = [new("field", "Current validation failed")] };
        var calls = 0;
        var result = ActionPlans.Revalidate(Plan, () => { calls++; return fresh; }, Mismatch);
        Assert.Same(fresh, result.Plan);
        Assert.Equal(fresh.Errors, result.Errors);
        Assert.False(result.Succeeded);
        Assert.Equal(1, calls);
        Assert.False(ActionPlans.Revalidate(fresh, () => fresh, Mismatch).Succeeded);
        var reorderedErrors = fresh with { Errors = [new("b", "second"), new("a", "first")] };
        Assert.False(ActionPlans.ContentEquals(reorderedErrors, reorderedErrors with { Errors = reorderedErrors.Errors.Reverse().ToArray() }));
    }

    [Theory]
    [InlineData(ActionPlanWarningPolicy.Merge)]
    [InlineData(ActionPlanWarningPolicy.UsePlan)]
    [InlineData(ActionPlanWarningPolicy.PreserveApplication)]
    public void ScalarApplicationPreservesOutcomesAndExplicitWarningPolicy(ActionPlanWarningPolicy policy)
    {
        var plan = Plan;
        var expected = new EditOutcome { Warnings = ["Application warning"], Errors = [new("b", "Rejected atomically")] };
        var stages = new List<string>();
        var result = ActionPlans.ApplyScalar(plan, () => { stages.Add("plan"); return Plan; }, Mismatch,
            fresh => { Assert.True(ActionPlans.ContentEquals(plan, fresh)); stages.Add("bind"); },
            fresh => { stages.Add("select"); return fresh.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray(); },
            edits => { stages.Add("apply"); Assert.Equal(2, edits.Count); return expected; }, policy);
        Assert.Equal(["plan", "bind", "select", "apply"], stages);
        Assert.Equal(expected.Errors, result.Errors);
        string[] warnings = policy switch
        {
            ActionPlanWarningPolicy.Merge => ["Experimental", "Integrity unknown", "Application warning"],
            ActionPlanWarningPolicy.UsePlan => ["Experimental", "Integrity unknown"],
            _ => ["Application warning"],
        };
        Assert.Equal(warnings, result.Warnings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PluginSelectsDerivedEdits_AndUnopenedFieldsRemainUndoable(bool includeDerived)
    {
        var document = new ScalarDocument();
        var plan = document.Plan(new());
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("a"));
        var outcome = ActionPlans.ApplyScalar(plan, () => document.Plan(new()), Mismatch, _ => document.Bind(),
            fresh => fresh.Changes.Where(change => includeDerived || !change.Derived)
                .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray(), document.ApplyEdits,
            ActionPlanWarningPolicy.Merge);
        Assert.True(outcome.Succeeded);
        Assert.Equal(includeDerived ? 2 : 1, outcome.Changes.Count);
        Assert.Equal(10, document.GetValue("a").AsInt64());
        Assert.Equal(includeDerived ? 20 : 2, document.GetValue("b").AsInt64());
        var undo = document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray());
        Assert.True(undo.Succeeded);
        Assert.Equal(1, document.GetValue("a").AsInt64());
        Assert.Equal(2, document.GetValue("b").AsInt64());
    }

    [Fact]
    public void NullAndEmptySelectionsAreNotNormalized_AndPreparationCannotRunOnMismatch()
    {
        var document = new ScalarDocument();
        var reviewed = document.Plan(new());
        var empty = new ActionScope([]);
        var result = ActionPlans.Revalidate(reviewed, () => document.Plan(empty), Mismatch);
        Assert.False(result.Succeeded);
        Assert.Empty(result.Plan.Changes);
        Assert.True(ActionPlans.Revalidate(result.Plan, () => document.Plan(empty), Mismatch).Succeeded);
    }

    private sealed class ScalarDocument : BoundSaveDocument
    {
        private readonly Dictionary<string, long> _values = new() { ["a"] = 1, ["b"] = 2 };
        public override string Title => "Scalar fixture";
        public override ViewNode BuildView() => new SectionNode("Lazy fixture", []);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public void Bind()
        {
            foreach (var key in _values.Keys)
                _ = Field(key, key, IntegerSpec.Int64, () => FieldValue.Of(_values[key]), value => _values[key] = value.AsInt64());
        }

        public ActionPlan Plan(ActionScope scope) => new()
        {
            Changes = (scope.RowKeys ?? _values.Keys.ToArray()).Select(key =>
                new PlannedChange(key, key, FieldValue.Of(_values[key]), FieldValue.Of(key == "a" ? 10L : 20L))
                { Derived = key == "b" }).ToArray(),
        };
    }
}
