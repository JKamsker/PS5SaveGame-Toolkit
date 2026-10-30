using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>Resolves current bindings and replays writable primaries, excluding derived aliases.</summary>
public static class FieldMetadataReplay
{
    public static IReadOnlyList<ValueEdit> Edits(ViewIndex index, IReadOnlyList<ValueChange> changes, bool useOld)
    {
        index.ResolveMetadata(changes.Select(change => change.ValueId), CancellationToken.None,
            FieldMetadataPlanning.MaxPlannedMetadataIds);
        var result = new List<ValueEdit>();
        var coalesced = new Dictionary<string, (FieldValue Old, FieldValue New)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var change in changes)
        {
            var metadata = index.ResolvedMetadata(change.ValueId);
            if (index.HasFieldMetadata && metadata is null && index.ResolvedSpec(change.ValueId) is null)
                throw new InvalidOperationException($"Missing replay metadata for '{change.ValueId}'.");
            if (metadata?.BindingKind == FieldBindingKind.Derived || index.ResolvedSpec(change.ValueId) is { ReadOnly: true }) continue;
            var canonical = metadata is { } resolved && !string.IsNullOrEmpty(resolved.CanonicalValueId)
                ? resolved.CanonicalValueId : change.ValueId;
            if (coalesced.TryGetValue(canonical, out var prior))
                coalesced[canonical] = (prior.Old, change.NewValue);
            else
            {
                coalesced.Add(canonical, (change.OldValue, change.NewValue));
                order.Add(canonical);
            }
        }

        foreach (var canonical in useOld ? order.AsEnumerable().Reverse() : order)
        {
            var values = coalesced[canonical];
            result.Add(new ValueEdit(canonical, useOld ? values.Old : values.New));
        }
        return result;
    }
}
