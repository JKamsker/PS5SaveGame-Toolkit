namespace SaveToolkit.Sdk;

using SaveToolkit.Sdk.Values;

/// <summary>How scalar application combines fresh preview warnings with application warnings, including rejections.</summary>
public enum ActionPlanWarningPolicy
{
    /// <summary>Keep only warnings returned by the application; rejected revalidation supplies none.</summary>
    PreserveApplication,
    /// <summary>Use only warnings from the fresh plan.</summary>
    UsePlan,
    /// <summary>Append application warnings after fresh-plan warnings, preserving order and duplicates.</summary>
    Merge,
}

/// <summary>The freshly calculated plan and validation errors, which may instead describe a reviewed-plan mismatch.</summary>
public sealed record ActionPlanValidation(ActionPlan Plan, IReadOnlyList<FieldError> Errors)
{
    /// <summary>True when the fresh plan has no errors and matches the reviewed content.</summary>
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
        return reviewed.Changes.Count == fresh.Changes.Count && reviewed.Changes.Zip(fresh.Changes).All(pair => ChangeEquals(pair.First, pair.Second)) && reviewed.SkippedRows.SequenceEqual(fresh.SkippedRows) &&
            reviewed.Warnings.SequenceEqual(fresh.Warnings, StringComparer.Ordinal) && reviewed.Errors.SequenceEqual(fresh.Errors);
    }

    private static bool ChangeEquals(PlannedChange first, PlannedChange second) =>
        first.ValueId == second.ValueId && first.Label == second.Label && first.OldValue == second.OldValue &&
        first.NewValue == second.NewValue && first.Derived == second.Derived && first.OldExists == second.OldExists &&
        first.NewExists == second.NewExists && EffectEquals(first.Effect, second.Effect);

    private static bool EffectEquals(ActionEffectDescriptor? first, ActionEffectDescriptor? second)
    {
        if (first is null || second is null) return first is null && second is null;
        return first.Kind == second.Kind && FieldSpecMetadata.Agree(first.Spec, second.Spec)
            && PresentationEquals(first.Presentation, second.Presentation)
            && PathEquals(first.Path, second.Path) && first.Source == second.Source;
    }

    private static bool PresentationEquals(FieldPresentation? first, FieldPresentation? second) =>
        first?.Confidence == second?.Confidence && first?.Warning == second?.Warning && first?.Description == second?.Description;

    private static bool PathEquals(IReadOnlyList<string>? first, IReadOnlyList<string>? second) =>
        first is null || second is null ? first is null && second is null : first.SequenceEqual(second, StringComparer.Ordinal);

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
