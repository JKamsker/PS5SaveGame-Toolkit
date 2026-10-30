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
        Admit(plan);
        return plan with
        {
            Changes = Array.AsReadOnly(SnapshotChanges(plan.Changes)),
            SkippedRows = Array.AsReadOnly(SnapshotRows(plan.SkippedRows)),
            Warnings = Array.AsReadOnly(SnapshotStrings(plan.Warnings)),
            Errors = Array.AsReadOnly(SnapshotErrors(plan.Errors)),
        };
    }

    private static PlannedChange[] SnapshotChanges(IEnumerable<PlannedChange> source) =>
        source.SelectBounded(Snapshot);
    private static SkippedActionRow[] SnapshotRows(IEnumerable<SkippedActionRow> source) =>
        source.SelectBounded(row => row with { });
    private static string[] SnapshotStrings(IEnumerable<string> source) =>
        source.SelectBounded(static value => value);
    private static FieldError[] SnapshotErrors(IEnumerable<FieldError> source) =>
        source.SelectBounded(error => error with { });

    private static T[] SelectBounded<T>(this IEnumerable<T> source, Func<T, T> copy)
    {
        var values = new List<T>();
        foreach (var item in source)
        {
            if (values.Count >= MaxPlanEntries) throw new InvalidOperationException("The action plan exceeds the bounded entry limit.");
            values.Add(copy(item));
        }
        return values.ToArray();
    }

    /// <summary>Returns the serialized UTF-8 size of a bounded plan.</summary>
    public static long SerializedSize(ActionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.SerializeToUtf8Bytes(plan).LongLength;
    }

    private static PlannedChange Snapshot(PlannedChange change)
    {
        if (change.Effect is not null && (change.ValueId.Length > MaxEffectIdLength || change.Label.Length > MaxEffectLabelLength))
            throw new InvalidOperationException("Action plan identity or label exceeds its bounded limit.");
        return change with { Effect = Snapshot(change.Effect) };
    }

    private static void Admit(ActionPlan plan)
    {
        long budget = 4 * 1024 * 1024;
        var count = 0;
        foreach (var change in plan.Changes)
        {
            if (++count > MaxPlanEntries) throw new InvalidOperationException("The action plan exceeds the bounded entry limit.");
            if (change.Effect is { } effect)
            {
                Add(ref budget, effect.Spec);
                Add(ref budget, change.ValueId);
                Add(ref budget, change.Label);
                Add(ref budget, effect.Source);
                if (effect.Path is { } path)
                {
                    AddBytes(ref budget, path.Count * 16L);
                    foreach (var part in path) Add(ref budget, part);
                }
                if (effect.Presentation is { } presentation)
                {
                    Add(ref budget, presentation.Warning);
                    Add(ref budget, presentation.Description);
                }
            }
        }
    }

    private static void Add(ref long remaining, FieldSpec spec)
    {
        if (!FieldSpecMetadata.TryEstimate(spec, remaining, out var cost))
            throw new InvalidOperationException("Action plan exceeds its bounded budget.");
        remaining -= cost;
    }

    private static void Add(ref long remaining, string? value)
    {
        if (value is null) return;
        var cost = checked(value.Length * 2L);
        if (cost > remaining) throw new InvalidOperationException("Action plan exceeds its bounded budget.");
        remaining -= cost;
    }

    private static void AddBytes(ref long remaining, long cost)
    {
        if (cost < 0 || cost > remaining) throw new InvalidOperationException("Action plan exceeds its bounded budget.");
        remaining -= cost;
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
        if (effect.Presentation?.Confidence is { } confidence && !Enum.IsDefined(confidence))
            throw new InvalidOperationException("Action effect confidence is invalid.");
        if (!effect.Spec.ReadOnly || !FieldSpecMetadata.TryEstimate(effect.Spec, 4 * 1024 * 1024, out _))
            throw new InvalidOperationException("Action effect specs must be bounded and read-only.");
        if (ContainsReferenceCallback(effect.Spec))
            throw new InvalidOperationException("Action effect references cannot execute eligibility callbacks.");
        return effect.Snapshot();
    }

    private static bool ContainsReferenceCallback(FieldSpec spec) => spec switch
    {
        ReferenceSpec { Assess: not null } => true,
        NullableSpec nullable => ContainsReferenceCallback(nullable.Inner),
        UnionSpec union => union.Options.Any(option => ContainsReferenceCallback(option.Spec)),
        _ => false,
    };

}
