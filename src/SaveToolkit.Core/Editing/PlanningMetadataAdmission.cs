using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>Stages the entire planning authority closure in wire units before equality, getters or snapshots.</summary>
internal static class PlanningMetadataAdmission
{
    public static IReadOnlyList<FieldMetadata> Resolve(ISaveDocument document, IReadOnlyList<string> ids,
        int maximumIds, long allowance, CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument source) return [];
        if (allowance < 2) throw new InvalidOperationException("Field metadata exceeds its bounded budget.");
        var fields = new Dictionary<string, FieldMetadata>(StringComparer.Ordinal);
        var pending = new Queue<string>(ids);
        var scheduled = ids.ToHashSet(StringComparer.Ordinal);
        // Even an empty segment needs two quotes and a separator on the wire. Derive
        // this traversal bound from the operation allowance, not the semantic-ID cap.
        var maximumPathParts = checked((int)Math.Min(int.MaxValue, allowance / 3 + 1));
        long bytes = 2; // JSON array brackets, including an empty closure.
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = pending.Take(FieldMetadataLimits.MaxBatchSize).ToArray();
            foreach (var _ in batch) pending.Dequeue();
            var requested = batch.ToHashSet(StringComparer.Ordinal);
            var response = source.ResolveFieldMetadata(Array.AsReadOnly(batch));
            foreach (var raw in BoundedMetadataCollections.Enumerate(response, batch.Length, cancellationToken))
            {
                if (raw is null || string.IsNullOrEmpty(raw.ValueId) || !requested.Remove(raw.ValueId)
                    || string.IsNullOrEmpty(raw.Label) || raw.Spec is null || !Enum.IsDefined(raw.BindingKind)
                    || raw.Presentation?.Confidence is { } confidence && !Enum.IsDefined(confidence)
                    || raw.BindingKind == FieldBindingKind.Derived && !raw.Spec.ReadOnly)
                    throw new InvalidOperationException("Invalid or duplicate field metadata.");
                var field = string.IsNullOrEmpty(raw.CanonicalValueId) ? raw with { CanonicalValueId = raw.ValueId } : raw;
                if (fields.Count > 0) bytes++;
                new ActionWireBudget(allowance - bytes, cancellationToken).AdmitMetadata(field);
                bytes += BoundedJson.Measure(field, checked((int)Math.Min(int.MaxValue, allowance - bytes)));
                fields.Add(field.ValueId, field);
                if (scheduled.Add(field.CanonicalValueId))
                {
                    if (scheduled.Count > maximumIds)
                        throw new InvalidOperationException("Canonical metadata closure exceeded its bounded limit.");
                    pending.Enqueue(field.CanonicalValueId);
                }
            }
        }
        // Every dependency has now fitted the same operation allowance. These operations may
        // invoke deep comparison or provider code, so none may run during partial admission.
        foreach (var field in fields.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FieldMetadataIndex.ValidateCanonical(field, fields);
        }
        foreach (var field in fields.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = document.GetValue(field.ValueId);
        }
        var detached = new List<FieldMetadata>(fields.Count);
        foreach (var field in fields.Values.OrderBy(field => field.ValueId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            detached.Add(field with
            {
                Spec = FieldSpecMetadata.SnapshotBounded(field.Spec, cancellationToken),
                Path = field.Path is { } path
                    ? Array.AsReadOnly(BoundedMetadataCollections.Copy(path, maximumPathParts, cancellationToken)) : null,
            });
        }
        cancellationToken.ThrowIfCancellationRequested();
        return detached.AsReadOnly();
    }
}
