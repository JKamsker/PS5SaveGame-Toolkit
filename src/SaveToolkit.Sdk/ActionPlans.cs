namespace SaveToolkit.Sdk;

public enum ActionPlanWarningPolicy
{
    PreserveApplication,
    UsePlan,
    Merge,
}

public sealed record ActionPlanValidation(ActionPlan Plan, IReadOnlyList<FieldError> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>Deterministic content revalidation for direct plugin callers; Core's token/revision gates still apply.</summary>
public static class ActionPlans
{
    /// <summary>Compares every ordered consequence, including labels, existence, derived flags and diagnostics.</summary>
    public static bool ContentEquals(ActionPlan reviewed, ActionPlan fresh)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(fresh);
        return reviewed.Changes.SequenceEqual(fresh.Changes) && reviewed.SkippedRows.SequenceEqual(fresh.SkippedRows) &&
            reviewed.Warnings.SequenceEqual(fresh.Warnings, StringComparer.Ordinal) && reviewed.Errors.SequenceEqual(fresh.Errors);
    }

    /// <summary>Replans once; fresh validation failures take precedence over the caller's stale-plan diagnostic.</summary>
    public static ActionPlanValidation Revalidate(ActionPlan reviewed, Func<ActionPlan> replan, FieldError mismatchError)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(replan);
        ArgumentNullException.ThrowIfNull(mismatchError);
        var fresh = replan();
        return new(fresh, fresh.Errors.Count > 0 ? fresh.Errors : ContentEquals(reviewed, fresh) ? [] : [mismatchError]);
    }

    /// <summary>
    /// Revalidates, binds unopened fields, selects plugin-authorized scalar edits, then applies atomically.
    /// Binding/selection must not change saved state; applyEdits owns rollback and reports all undo changes.
    /// Derived changes are never filtered here. This helper does not interpret or normalize action scopes.
    /// </summary>
    public static EditOutcome ApplyScalar(ActionPlan reviewed, Func<ActionPlan> replan, FieldError mismatchError,
        Action<ActionPlan> prepareBindings, Func<ActionPlan, IReadOnlyList<ValueEdit>> selectEdits,
        Func<IReadOnlyList<ValueEdit>, EditOutcome> applyEdits, ActionPlanWarningPolicy warningPolicy)
    {
        ArgumentNullException.ThrowIfNull(prepareBindings);
        ArgumentNullException.ThrowIfNull(selectEdits);
        ArgumentNullException.ThrowIfNull(applyEdits);
        if (!Enum.IsDefined(warningPolicy)) throw new ArgumentOutOfRangeException(nameof(warningPolicy));
        var validation = Revalidate(reviewed, replan, mismatchError);
        var fresh = validation.Plan;
        EditOutcome result;
        if (!validation.Succeeded)
            result = new() { Errors = validation.Errors };
        else
        {
            prepareBindings(fresh);
            var edits = selectEdits(fresh);
            result = applyEdits(edits);
        }

        return result with
        {
            Warnings = warningPolicy switch
            {
                ActionPlanWarningPolicy.UsePlan => fresh.Warnings,
                ActionPlanWarningPolicy.Merge => [.. fresh.Warnings, .. result.Warnings],
                _ => result.Warnings,
            },
        };
    }
}
