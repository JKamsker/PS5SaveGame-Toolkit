using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>Checks canonical binding policy before a preview is usable, and again before apply.</summary>
public static class FieldMetadataPlanning
{
    // Matches DocumentSession's existing 5000-row action-scope policy. Semantic review keeps
    // its stricter 4096 retained-target cap, while atomic action preflight validates this
    // already-advertised scope without silently truncating it.
    private const int MaxPlannedMetadataIds = 65_536;

    /// <summary>Legacy documents return no descriptors and retain their existing planning behavior.</summary>
    public static IReadOnlyList<FieldMetadata> Validate(ViewIndex index, ISaveDocument document, ActionPlan plan,
        ReferenceFieldService? references = null)
        => Validate(index, document, plan, references, CancellationToken.None);

    internal static IReadOnlyList<FieldMetadata> Validate(ViewIndex index, ISaveDocument document, ActionPlan plan,
        ReferenceFieldService? references, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effects = plan.Changes.Where(change => change.Effect is not null).ToArray();
        var effectEnabled = document switch
        {
            StagedGroupDocument staged => staged.EffectEnabled,
            IActionEffectDocument => true,
            _ => false,
        };
        if (effects.Length > 0 && !effectEnabled)
            throw new InvalidOperationException("Action effects require an opted-in document capability.");
        if (!index.HasFieldMetadata && effects.Length == 0) return [];
        index.ResolveMetadata(plan.Changes.Select(change => change.ValueId), cancellationToken,
            MaxPlannedMetadataIds);
        var result = new List<FieldMetadata>(plan.Changes.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in plan.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ids.Add(change.ValueId)) throw new InvalidOperationException($"Duplicate planned field '{change.ValueId}'.");
            var metadata = index.ResolvedMetadata(change.ValueId);
            var rendered = index.Spec(change.ValueId);
            if (change.Effect is not null)
            {
                if (metadata is not null || rendered is not null)
                    throw new InvalidOperationException($"Action effect cannot shadow canonical field '{change.ValueId}'.");
                ValidateEffect(change);
                continue;
            }
            metadata ??= index.HasFieldMetadata
                ? throw new InvalidOperationException($"Missing field metadata for '{change.ValueId}'.")
                : null;
            if (metadata is null) continue;
            if (change.Derived != (metadata.BindingKind == FieldBindingKind.Derived)
                || (!change.Derived && metadata.Spec.ReadOnly))
                throw new InvalidOperationException($"Planned binding policy conflicts for '{change.ValueId}'.");
            if (change.OldExists && document.GetValue(change.ValueId) != change.OldValue)
                throw new InvalidOperationException($"Planned original value conflicts for '{change.ValueId}'.");
            if (change.NewExists)
            {
                var validated = FieldValueCoercer.Normalize(change.NewValue, metadata.Spec,
                    allowReadOnly: change.Derived, references: references);
                if (!validated.Ok) throw new InvalidOperationException($"Invalid planned field '{change.ValueId}': {validated.Error}");
            }
            result.Add(metadata);
        }
        return result;
    }

    private static void ValidateEffect(PlannedChange change)
    {
        var effect = change.Effect!;
        if (effect.Kind is not (ActionEffectKind.Observation or ActionEffectKind.StructuralMemento)
            || !effect.Spec.ReadOnly || effect.Path is { Count: > ActionPlanSnapshots.MaxEffectPathParts })
            throw new InvalidOperationException($"Invalid action effect descriptor for '{change.ValueId}'.");
        if (!FieldSpecMetadata.TryEstimate(effect.Spec, 4 * 1024 * 1024, out _))
            throw new InvalidOperationException($"Action effect spec exceeds its bounded limit for '{change.ValueId}'.");
        if (effect.Spec is ReferenceSpec { Assess: not null })
            throw new InvalidOperationException($"Action effect reference policy must be display-only for '{change.ValueId}'.");
        if (change.OldExists == change.NewExists && change.OldExists && change.OldValue == change.NewValue
            || !change.OldExists && !change.NewExists)
            throw new InvalidOperationException($"Action effect '{change.ValueId}' does not describe a change.");
        ValidateSide(change.OldValue, change.OldExists, effect.Spec, change.ValueId);
        ValidateSide(change.NewValue, change.NewExists, effect.Spec, change.ValueId);
        if (effect.Kind == ActionEffectKind.Observation && (!change.OldExists || !change.NewExists))
            throw new InvalidOperationException($"Observation effect '{change.ValueId}' cannot describe absence.");
    }

    private static void ValidateSide(FieldValue value, bool exists, FieldSpec spec, string id)
    {
        if (!exists)
        {
            if (!value.IsNull) throw new InvalidOperationException($"Absent effect '{id}' must use null.");
            return;
        }
        if (!ExactShape(value, spec)) throw new InvalidOperationException($"Effect value type does not match '{id}'.");
        if (ContainsReference(spec)) return;
        var result = FieldValueCoercer.Normalize(value, spec, allowReadOnly: true,
            references: null);
        if (!result.Ok || result.Value != value)
            throw new InvalidOperationException($"Effect value is invalid for '{id}': {result.Error ?? "type normalization changed the value"}.");
    }

    private static bool ContainsReference(FieldSpec spec) => spec switch
    {
        ReferenceSpec => true,
        NullableSpec nullable => ContainsReference(nullable.Inner),
        UnionSpec union => union.Options.Any(option => ContainsReference(option.Spec)),
        _ => false,
    };

    private static bool ExactShape(FieldValue value, FieldSpec spec) => spec switch
    {
        NullableSpec nullable => value.IsNull || ExactShape(value, nullable.Inner),
        UnionSpec union => value.Kind == FieldValueKind.Union && union.Options.Any(option =>
            option.Id == value.AsUnion().Type && ExactShape(value.AsUnion().Value, option.Spec)),
        BoolSpec => value.Kind == FieldValueKind.Bool,
        IntegerSpec or FlagsSpec => value.Kind == FieldValueKind.Integer,
        FloatSpec => value.Kind == FieldValueKind.Float,
        TextSpec or EnumSpec or DateTimeSpec or ReferenceSpec => value.Kind == FieldValueKind.Text,
        ExactNumberSpec => value.Kind == FieldValueKind.ExactNumber,
        _ => false,
    };

    /// <summary>Includes labels, paths, specs and binding kinds, so metadata drift retires a preview.</summary>
    public static bool Agree(IReadOnlyList<FieldMetadata> before, IReadOnlyList<FieldMetadata> after) =>
        FieldMetadataIndex.Agree(before, after);
}
