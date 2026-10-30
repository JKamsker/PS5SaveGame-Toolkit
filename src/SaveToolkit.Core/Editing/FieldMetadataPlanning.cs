using SaveToolkit.Core.References;
using SaveToolkit.Sdk;

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
        ReferenceFieldService? references, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!index.HasFieldMetadata) return [];
        index.ResolveMetadata(plan.Changes.Select(change => change.ValueId), cancellationToken,
            MaxPlannedMetadataIds);
        var result = new List<FieldMetadata>(plan.Changes.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in plan.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = index.ResolvedMetadata(change.ValueId)
                ?? throw new InvalidOperationException($"Missing field metadata for '{change.ValueId}'.");
            if (!ids.Add(change.ValueId)) throw new InvalidOperationException($"Duplicate planned field '{change.ValueId}'.");
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

    /// <summary>Includes labels, paths, specs and binding kinds, so metadata drift retires a preview.</summary>
    public static bool Agree(IReadOnlyList<FieldMetadata> before, IReadOnlyList<FieldMetadata> after) =>
        FieldMetadataIndex.Agree(before, after);
}
