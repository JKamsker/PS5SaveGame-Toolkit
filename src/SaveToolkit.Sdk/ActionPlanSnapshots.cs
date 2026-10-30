using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk;

/// <summary>Bounded detached snapshots used for action preview retention and structural equality.</summary>
public static class ActionPlanSnapshots
{
    /// <summary>Maximum inclusive default JSON bytes retained by a standalone plan.</summary>
    public const int MaxSerializedBytes = 4 * 1024 * 1024;
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


    /// <summary>Creates a detached snapshot after admitting every actual collection traversal.</summary>
    public static ActionPlan Snapshot(ActionPlan plan, CancellationToken cancellationToken = default) =>
        Snapshot(plan, MaxSerializedBytes, cancellationToken);

    /// <summary>Creates a detached snapshot within the caller's remaining inclusive wire allowance.</summary>
    public static ActionPlan Snapshot(ActionPlan plan, long maximumBytes, CancellationToken cancellationToken = default)
    {
        Validate(plan, maximumBytes, cancellationToken);
        return plan with
        {
            Changes = Array.AsReadOnly(BoundedMetadataCollections.Enumerate(plan.Changes, MaxPlanEntries, cancellationToken)
                .Select(change => change with { Effect = SnapshotEffect(change.Effect, cancellationToken) }).ToArray()),
            SkippedRows = Array.AsReadOnly(BoundedMetadataCollections.Copy(plan.SkippedRows, MaxPlanEntries, cancellationToken)),
            Warnings = Array.AsReadOnly(BoundedMetadataCollections.Copy(plan.Warnings, MaxPlanEntries, cancellationToken)),
            Errors = Array.AsReadOnly(BoundedMetadataCollections.Copy(plan.Errors, MaxPlanEntries, cancellationToken)),
        };
    }

    /// <summary>Returns the serialized UTF-8 size of a bounded plan.</summary>
    public static long SerializedSize(ActionPlan plan) => Admit(plan, MaxSerializedBytes, CancellationToken.None);

    /// <summary>Measures exact default JSON bytes after admission within a finite caller allowance.</summary>
    public static long SerializedSize(ActionPlan plan, long maximumBytes, CancellationToken cancellationToken = default) =>
        Admit(plan, maximumBytes, cancellationToken);

    /// <summary>Admits finite graph shape before serialization, authority lookup or detached copies.</summary>
    public static void Validate(ActionPlan plan, CancellationToken cancellationToken = default) =>
        _ = Admit(plan, MaxSerializedBytes, cancellationToken);

    /// <summary>Admits shape and wire bytes within the caller's finite remaining allowance.</summary>
    public static void Validate(ActionPlan plan, long maximumBytes, CancellationToken cancellationToken = default) =>
        _ = Admit(plan, maximumBytes, cancellationToken);

    private static long Admit(ActionPlan plan, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var total = checked((long)plan.Changes.Count + plan.SkippedRows.Count + plan.Warnings.Count + plan.Errors.Count);
        if (total > MaxPlanEntries) throw new InvalidOperationException("The action plan exceeds the bounded entry limit.");
        new ActionWireBudget(maximumBytes, cancellationToken).AdmitPlan(plan);
        BoundedMetadataCollections.Visit(plan.SkippedRows, MaxPlanEntries, row => ArgumentNullException.ThrowIfNull(row), cancellationToken);
        BoundedMetadataCollections.Visit(plan.Warnings, MaxPlanEntries, value => ArgumentNullException.ThrowIfNull(value), cancellationToken);
        BoundedMetadataCollections.Visit(plan.Errors, MaxPlanEntries, value => ArgumentNullException.ThrowIfNull(value), cancellationToken);
        BoundedMetadataCollections.Visit(plan.Changes, MaxPlanEntries, change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            if (change.Effect is { } effect)
            {
                if (change.ValueId.Length > MaxEffectIdLength || change.Label.Length > MaxEffectLabelLength)
                    throw new InvalidOperationException("Action plan identity or label exceeds its bounded limit.");
                ValidateEffect(effect, cancellationToken);
            }
        }, cancellationToken);
        return ActionWireJson.Measure(plan, maximumBytes);
    }

    private static void ValidateEffect(ActionEffectDescriptor effect, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(effect.Kind) || effect.Source?.Length > MaxEffectSourceLength)
            throw new InvalidOperationException("Action effect kind or source exceeds its bounded limit.");
        if (effect.Path is { } path)
            BoundedMetadataCollections.Visit(path, MaxEffectPathParts, part =>
            {
                if (part is null || part.Length > MaxEffectPathPartLength)
                    throw new InvalidOperationException("Action effect path exceeds its bounded limit.");
            }, cancellationToken);
        if (effect.Presentation is { Warning.Length: > MaxPresentationTextLength }
            || effect.Presentation is { Description.Length: > MaxPresentationTextLength })
            throw new InvalidOperationException("Action effect presentation exceeds its bounded limit.");
        if (effect.Presentation?.Confidence is { } confidence && !Enum.IsDefined(confidence))
            throw new InvalidOperationException("Action effect confidence is invalid.");
        if (!effect.Spec.ReadOnly)
            throw new InvalidOperationException("Action effect specs must be bounded and read-only.");

    }

    private static ActionEffectDescriptor? SnapshotEffect(ActionEffectDescriptor? effect, CancellationToken cancellationToken) =>
        effect is null ? null : effect with
        {
            Spec = FieldSpecMetadata.SnapshotBounded(effect.Spec, cancellationToken),
            Path = effect.Path is { } path ? BoundedMetadataCollections.Path(path, cancellationToken) : null,
            Presentation = effect.Presentation is { } presentation ? presentation with { } : null,
        };

}
