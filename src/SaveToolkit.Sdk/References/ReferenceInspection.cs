using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk.References;

/// <summary>Construction and exact-selection validation for attributed, non-mutating reference inspection actions.</summary>
public static class ReferenceInspection
{
    /// <summary>
    /// Creates a non-mutating, attributed reference picker action; absent/empty catalogs yield null.
    /// Explicit initial keys must belong to the pinned catalog. Description carries plugin-specific evidence caveats.
    /// </summary>
    public static ActionNode? Create(IReferenceCatalog? catalog, string actionId, string label,
        string parameterId, string parameterLabel, string description, string? initialKey = null, bool fixedRecord = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(actionId);
        ArgumentException.ThrowIfNullOrEmpty(parameterId);
        if (catalog is null) return null;
        var record = initialKey is null ? catalog.Search(new() { Limit = 1 }).Items.FirstOrDefault()
            : catalog.Find(initialKey) ?? throw new ArgumentException("Initial reference key is not in the pinned catalog.", nameof(initialKey));
        if (record is null) return null;
        return new(actionId, label)
        {
            Mutates = false,
            Tone = Tone.Info,
            Description = $"{description} {record.AttributionText}",
            Parameters = [new FieldNode(parameterId, parameterLabel, new ReferenceSpec(catalog.Id) { ReadOnly = fixedRecord },
                FieldValue.Of(record.Key))],
        };
    }

    /// <summary>
    /// Requires exactly one text parameter and checks ordinal membership in the supplied pinned catalog.
    /// A read-only picker is presentation only; pass expectedKey to enforce a fixed-record action for direct callers.
    /// </summary>
    public static ReferenceRecord ResolveSelection(IReferenceCatalog? catalog,
        IReadOnlyDictionary<string, FieldValue> parameters, string parameterId, string? expectedKey = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrEmpty(parameterId);
        if (catalog is null) throw new InvalidOperationException("No pinned reference catalog is available.");
        if (parameters.Count != 1 || !parameters.TryGetValue(parameterId, out var value) || value.Kind != FieldValueKind.Text ||
            expectedKey is not null && !string.Equals(value.AsString(), expectedKey, StringComparison.Ordinal))
            throw new ArgumentException("Choose exactly one existing catalog record.", nameof(parameters));
        return catalog.Find(value.AsString()) ??
            throw new ArgumentException("The selected reference is not in the pinned catalog.", nameof(parameters));
    }
}
