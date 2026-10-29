using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk;

/// <summary>The canonical replay policy for a stable value id.</summary>
public enum FieldBindingKind
{
    /// <summary>An independent value. Writable primaries alone are replayed during undo/redo.</summary>
    Primary,
    /// <summary>A read-only consequence or alias, recomputed atomically by primary setters.</summary>
    Derived,
}

/// <summary>Canonical metadata, independent of any rendered field projection.</summary>
/// <param name="ValueId">Nonempty, stable document value ID.</param>
/// <param name="Label">Nonempty canonical human label.</param>
/// <param name="Spec">Canonical spec; nested collections must not be mutated after publication.</param>
public sealed record FieldMetadata(string ValueId, string Label, FieldSpec Spec)
{
    /// <summary>Derived fields must publish a read-only spec; this does not add write permission.</summary>
    public FieldBindingKind BindingKind { get; init; }

    /// <summary>Optional real view navigation path. Null means no known navigation target.</summary>
    public IReadOnlyList<string>? Path { get; init; }
}

/// <summary>Optional, targeted field discovery. Implement explicitly to opt in; never enumerate a view.</summary>
/// <remarks>Called under the host's document lock. Direct callers must serialize access with other document operations.</remarks>
public interface IFieldMetadataDocument
{
    /// <summary>
    /// Resolves at most <see cref="FieldMetadataLimits.MaxBatchSize"/> requested stable IDs against the current model. Omit unknown IDs.
    /// Results must be unique, immutable and limited to requested ids. Each descriptor guarantees
    /// GetValue works and writable primaries have live setters and plugin validation available.
    /// Registration may update bookkeeping but must not change save values or render/expand views.
    /// Re-resolve after structure changes, restores and group publication; never retain old-model setters.
    /// A plan must describe every affected id, including read-only derived consequences.
    /// </summary>
    /// <param name="valueIds">Non-null list of nonempty IDs; duplicates count toward the batch limit.</param>
    /// <returns>Unique requested descriptors, or an empty list when none are known.</returns>
    IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds);
}
