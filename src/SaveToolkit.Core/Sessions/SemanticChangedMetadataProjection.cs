using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

/// <summary>Enriches only current rows whose value is proven to have changed.</summary>
internal static class SemanticChangedMetadataProjection
{
    public static void Apply(SemanticSnapshot original, SemanticSnapshot current, ISaveDocument document,
        long maxBytes, CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument) return;

        var ids = current.Fields.Values
            .Where(field => !field.MetadataPublished
                && (!original.Fields.TryGetValue(field.ValueId, out var before) || before.Value != field.Value))
            .Select(field => field.ValueId)
            .Distinct(StringComparer.Ordinal)
            .Take(FieldMetadataLimits.MaxClosureIds + 1)
            .ToArray();
        if (ids.Length == 0) return;
        if (ids.Length > FieldMetadataLimits.MaxClosureIds)
        {
            current.Complete = false;
            current.OmittedCount++;
            current.OmittedCountIsLowerBound = true;
            current.QueryWarnings = [.. current.QueryWarnings,
                $"Changed-field metadata projection was bounded at {FieldMetadataLimits.MaxClosureIds} identities."];
            ids = ids[..FieldMetadataLimits.MaxClosureIds];
        }

        var index = new FieldMetadataIndex(document, Math.Max(0, maxBytes - current.Bytes));
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { index.ResolveAll([id], cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (OutOfMemoryException) { throw; }
            catch (Exception ex)
            {
                current.Complete = false;
                current.QueryWarnings = [.. current.QueryWarnings,
                    $"Changed-field metadata coverage is incomplete for '{id}': {ex.Message}"];
                continue;
            }

            if (index.Cached(id) is not { } metadata || !current.Fields.TryGetValue(id, out var field)) continue;
            var updated = field with
            {
                Label = metadata.Label,
                Path = metadata.Path?.ToArray() ?? field.Path,
                ReadOnly = metadata.Spec.ReadOnly,
                CanonicalValueId = string.IsNullOrEmpty(metadata.CanonicalValueId) ? id : metadata.CanonicalValueId,
                BindingKind = metadata.BindingKind,
                Spec = metadata.Spec,
                Presentation = metadata.Presentation,
                MetadataPublished = true,
            };
            if (!SemanticSnapshot.TryEstimate(updated, long.MaxValue, out var updatedCost)
                || !SemanticSnapshot.TryEstimate(field, long.MaxValue, out var existingCost))
            {
                current.Complete = false;
                current.QueryWarnings = [.. current.QueryWarnings,
                    $"Changed-field metadata coverage is incomplete for '{id}': metadata could not be bounded."];
                continue;
            }

            var additional = updatedCost - existingCost;
            if (additional > 0 && additional > maxBytes - current.Bytes)
            {
                current.Complete = false;
                current.OmittedCount++;
                current.OmittedCountIsLowerBound = true;
                current.QueryWarnings = [.. current.QueryWarnings,
                    $"Changed-field metadata coverage omitted '{id}' because the review budget was exhausted."];
                continue;
            }

            current.Bytes += Math.Max(0, additional);
            current.Fields[id] = updated with { Spec = FieldSpecMetadata.Snapshot(metadata.Spec) };
        }
    }
}
