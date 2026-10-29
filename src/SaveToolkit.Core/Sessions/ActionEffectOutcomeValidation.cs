using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Checks the complete ordered effect projection before a mutation becomes visible.</summary>
internal static class ActionEffectOutcomeValidation
{
    public static void Validate(ActionPlan plan, EditOutcome outcome)
    {
        var effects = plan.Changes.Where(change => change.Effect is not null).ToArray();
        if (effects.Length == 0) return;
        if (outcome.Revert is null || !outcome.StructureChanged)
            throw new InvalidOperationException("An action with effects must return a structural revert token.");
        var cursor = 0;
        foreach (var expected in effects)
        {
            var actual = outcome.Changes.Skip(cursor).FirstOrDefault(change => change.ValueId == expected.ValueId);
            if (actual is null || actual.OldValue != expected.OldValue || actual.NewValue != expected.NewValue)
                throw new InvalidOperationException($"Action outcome omitted or changed effect '{expected.ValueId}'.");
            var next = -1;
            for (var i = cursor; i < outcome.Changes.Count; i++)
                if (ReferenceEquals(outcome.Changes[i], actual)) { next = i; break; }
            if (next < 0) throw new InvalidOperationException("Action outcome effect order is invalid.");
            cursor = next + 1;
        }
    }
}
