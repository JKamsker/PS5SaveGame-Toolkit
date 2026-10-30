using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Checks the complete ordered effect projection before a mutation becomes visible.</summary>
internal static class ActionEffectOutcomeValidation
{
    public static void Validate(ActionPlan plan, EditOutcome outcome)
    {
        var effects = plan.Changes.Where(change => change.Effect is not null).ToArray();
        if (effects.Length == 0) return;
        if (outcome.Errors.Count > 0 || outcome.Changes.Count != plan.Changes.Count)
            throw new InvalidOperationException("An action effect outcome must report the complete reviewed change projection.");
        var providerStructural = outcome.StructureChanged || outcome.Revert is not null;
        for (var i = 0; i < plan.Changes.Count; i++)
        {
            var expected = plan.Changes[i];
            var actual = outcome.Changes[i];
            if (actual.ValueId != expected.ValueId || actual.OldValue != expected.OldValue || actual.NewValue != expected.NewValue)
                throw new InvalidOperationException($"Action outcome omitted, reordered or changed '{expected.ValueId}'.");
        }
        var requiresProviderToken = effects.Any(change => change.Effect!.Kind == ActionEffectKind.StructuralMemento
            || change.OldExists != change.NewExists);
        if (requiresProviderToken && (outcome.Revert is null || !outcome.StructureChanged))
            throw new InvalidOperationException("A structural action effect must return a structural revert token.");
        if (providerStructural && outcome.Revert is null)
            throw new InvalidOperationException("A structurally changed action effect must return a revert token.");
    }
}
