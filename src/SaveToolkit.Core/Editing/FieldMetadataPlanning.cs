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
    internal const int MaxPlannedMetadataIds = 5000;

    /// <summary>Legacy documents return no descriptors and retain their existing planning behavior.</summary>
    public static IReadOnlyList<FieldMetadata> Validate(ViewIndex index, ISaveDocument document, ActionPlan plan,
        ReferenceFieldService? references = null)
        => Validate(index, document, plan, references, CancellationToken.None);

    internal static IReadOnlyList<FieldMetadata> Validate(ViewIndex index, ISaveDocument document, ActionPlan plan,
        ReferenceFieldService? references, CancellationToken cancellationToken, long? metadataBudget = null)
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
        var canonicalIds = plan.Changes.Where(change => change.Effect is null).Select(change => change.ValueId);
        // Effect-only plans still probe the canonical resolver for a bounded prefix so an
        // authority failure cannot be mistaken for absence. Large effect plans remain independent
        // of the canonical 5000-row metadata allowance.
        var resolverIds = canonicalIds.Concat(plan.Changes.Where(change => change.Effect is not null)
            .Take(FieldMetadataIndex.BatchSize).Select(change => change.ValueId)).Distinct(StringComparer.Ordinal);
        index.SetMetadataBudget(metadataBudget ?? 4 * 1024 * 1024);
        try
        {
            index.ResolveMetadata(resolverIds, cancellationToken, MaxPlannedMetadataIds);
        }
        finally
        {
            index.SetMetadataBudget(null);
        }
        var result = new List<FieldMetadata>(plan.Changes.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in plan.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ids.Add(change.ValueId)) throw new InvalidOperationException($"Duplicate planned field '{change.ValueId}'.");
            var metadata = index.ResolvedMetadata(change.ValueId);
            // Spec() is a resolver entry point. It can intentionally hide a registered
            // field after a later omission, so it cannot establish rendered authority.
            var rendered = index.HasRenderedField(change.ValueId);
            if (change.Effect is not null)
            {
                if (metadata is not null || rendered)
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
        if (!ValidDescriptor(effect))
            throw new InvalidOperationException($"Action effect spec exceeds its bounded limit for '{change.ValueId}'.");
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
        var result = ValidateTypedValue(value, spec);
        if (!result.Ok)
            throw new InvalidOperationException($"Effect value is invalid for '{id}': {result.Error}.");
    }

    private static bool ValidDescriptor(ActionEffectDescriptor effect)
    {
        if (!Enum.IsDefined(effect.Kind) || !effect.Spec.ReadOnly
            || effect.Path is { Count: > ActionPlanSnapshots.MaxEffectPathParts }
            || effect.Path?.Any(part => part is null || part.Length > ActionPlanSnapshots.MaxEffectPathPartLength) == true
            || effect.Source?.Length > ActionPlanSnapshots.MaxEffectSourceLength
            || effect.Presentation?.Warning?.Length > ActionPlanSnapshots.MaxPresentationTextLength
            || effect.Presentation?.Description?.Length > ActionPlanSnapshots.MaxPresentationTextLength
            || effect.Presentation?.Confidence is { } confidence && !Enum.IsDefined(confidence)) return false;
        try
        {
            return ValidateSpec(effect.Spec, 0, new HashSet<FieldSpec>(ReferenceEqualityComparer.Instance));
        }
        catch (Exception) { return false; }
    }

    private static bool ValidateSpec(FieldSpec spec, int depth, HashSet<FieldSpec> seen)
    {
        if (depth > FieldMetadataLimits.MaxSpecDepth || !seen.Add(spec)
            || !FieldSpecMetadata.TryEstimate(spec, 4 * 1024 * 1024, out _)) return false;
        var valid = spec switch
        {
            IntegerSpec integer => integer.Min <= integer.Max && Enum.IsDefined(integer.Format)
                && (integer.Format != IntegerFormat.Duration || integer.UnitsPerSecond is > 0),
            FloatSpec floating => Enum.IsDefined(floating.Format) && double.IsFinite(floating.UnitsPerSecond)
                && floating.UnitsPerSecond > 0 && floating.Min <= floating.Max,
            TextSpec text => text.MaxLength is not < 0,
            EnumSpec choice => choice.Options.Count > 0
                && choice.Options.Select(option => option.Value).Distinct(StringComparer.Ordinal).Count() == choice.Options.Count,
            FlagsSpec flags => flags.BitWidth is >= 1 and <= 127,
            ExactNumberSpec exact => exact.MaxLength is >= 1 and <= ExactJsonNumber.HardMaxLength
                && exact.MaxAbsoluteExponent is >= 0 and <= ExactJsonNumber.HardMaxAbsoluteExponent,
            ReferenceSpec reference => reference.Assess is null,
            NullableSpec nullable => ValidateSpec(nullable.Inner, depth + 1, seen),
            UnionSpec union => union.Options.Count > 0
                && union.Options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count() == union.Options.Count
                && union.Options.All(option => ValidateSpec(option.Spec, depth + 1, seen)),
            _ => true,
        };
        seen.Remove(spec);
        return valid;
    }

    private static FieldValueCoercer.Result ValidateTypedValue(FieldValue value, FieldSpec spec) => spec switch
    {
        ReferenceSpec => value.Kind == FieldValueKind.Text
            ? new(value, null) : new(FieldValue.Null, "Expected reference text."),
        NullableSpec nullable when value.IsNull => new(value, null),
        NullableSpec nullable => ValidateTypedValue(value, nullable.Inner),
        UnionSpec union when value.Kind == FieldValueKind.Union => ValidateUnionValue(value, union),
        UnionSpec => new(FieldValue.Null, "Expected a union value."),
        _ => FieldValueCoercer.Normalize(value, spec, allowReadOnly: true, references: null),
    };

    private static FieldValueCoercer.Result ValidateUnionValue(FieldValue value, UnionSpec spec)
    {
        var selected = value.AsUnion();
        var option = spec.Options.SingleOrDefault(candidate => candidate.Id == selected.Type);
        if (option is null) return new(FieldValue.Null, "Unknown union option.");
        var result = ValidateTypedValue(selected.Value, option.Spec);
        return result.Ok ? new(value, null) : result;
    }

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
