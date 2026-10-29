using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk;

/// <summary>Bounded detached snapshots used for action preview retention and structural equality.</summary>
public static class ActionPlanSnapshots
{
    /// <summary>Maximum aggregate number of plan rows and diagnostics.</summary>
    public const int MaxPlanEntries = 65_536;
    /// <summary>Maximum effect value identifier length.</summary>
    public const int MaxEffectIdLength = 1_024;
    /// <summary>Maximum effect label length.</summary>
    public const int MaxEffectLabelLength = 512;
    /// <summary>Maximum effect source length.</summary>
    public const int MaxEffectSourceLength = 2_000;
    /// <summary>Maximum effect navigation path part count.</summary>
    public const int MaxEffectPathParts = 32;
    /// <summary>Maximum effect navigation path part length.</summary>
    public const int MaxEffectPathPartLength = 200;
    /// <summary>Maximum warning or description length in effect presentation.</summary>
    public const int MaxPresentationTextLength = 4_096;

    /// <summary>Creates a detached, bounded snapshot of a plan.</summary>
    public static ActionPlan Snapshot(ActionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var total = checked((long)plan.Changes.Count + plan.SkippedRows.Count + plan.Warnings.Count + plan.Errors.Count);
        if (total > MaxPlanEntries) throw new InvalidOperationException("The action plan exceeds the bounded entry limit.");
        return plan with
        {
            Changes = Array.AsReadOnly(plan.Changes.Select(Snapshot).ToArray()),
            SkippedRows = Array.AsReadOnly(plan.SkippedRows.Select(row => row with { }).ToArray()),
            Warnings = Array.AsReadOnly(plan.Warnings.ToArray()),
            Errors = Array.AsReadOnly(plan.Errors.Select(error => error with { }).ToArray()),
        };
    }

    /// <summary>Returns the serialized UTF-8 size of a bounded plan.</summary>
    public static long SerializedSize(ActionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.SerializeToUtf8Bytes(plan).LongLength;
    }

    private static PlannedChange Snapshot(PlannedChange change)
    {
        if (change.ValueId.Length > MaxEffectIdLength || change.Label.Length > MaxEffectLabelLength)
            throw new InvalidOperationException("Action plan identity or label exceeds its bounded limit.");
        return change with { Effect = Snapshot(change.Effect) };
    }

    private static ActionEffectDescriptor? Snapshot(ActionEffectDescriptor? effect)
    {
        if (effect is null) return null;
        if (effect.Source?.Length > MaxEffectSourceLength)
            throw new InvalidOperationException("Action effect source exceeds its bounded limit.");
        if (effect.Path is { } path)
        {
            if (path.Count > MaxEffectPathParts || path.Any(part => part is null || part.Length > MaxEffectPathPartLength))
                throw new InvalidOperationException("Action effect path exceeds its bounded limit.");
        }
        if (effect.Presentation is { Warning.Length: > MaxPresentationTextLength }
            || effect.Presentation is { Description.Length: > MaxPresentationTextLength })
            throw new InvalidOperationException("Action effect presentation exceeds its bounded limit.");
        if (!effect.Spec.ReadOnly || !FieldSpecMetadata.TryEstimate(effect.Spec, 4 * 1024 * 1024, out _))
            throw new InvalidOperationException("Action effect specs must be bounded and read-only.");
        if (effect.Spec is ReferenceSpec { Assess: not null })
            throw new InvalidOperationException("Action effect references cannot execute eligibility callbacks.");
        return effect.Snapshot();
    }

}
