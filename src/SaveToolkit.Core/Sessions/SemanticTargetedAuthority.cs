using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

internal static class SemanticTargetedAuthority
{
    public static void Validate(SemanticSnapshot original, SemanticSnapshot current)
    {
        foreach (var (id, before) in original.Fields)
        {
            if (before.CanonicalValueId is null || !current.Fields.TryGetValue(id, out var after)) continue;
            if (string.Equals(before.CanonicalValueId, after.CanonicalValueId, StringComparison.Ordinal)
                && before.BindingKind == after.BindingKind && before.ReadOnly == after.ReadOnly
                && before.Spec is { } beforeSpec && after.Spec is { } afterSpec
                && FieldSpecMetadata.Agree(beforeSpec, afterSpec)) continue;
            current.Fields.Remove(id);
            current.Complete = false;
            current.QueryWarnings = [.. current.QueryWarnings,
                $"Targeted metadata coverage is incomplete: authority changed for '{id}'; revert is unavailable."];
        }
    }

    public static void ApplyCanonicalMetadata(ISaveDocument document, SemanticSnapshot snapshot,
        long maxBytes, CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument || snapshot.Fields.Count == 0) return;
        var index = new FieldMetadataIndex(document);
        foreach (var batch in snapshot.Fields.Keys.Chunk(FieldMetadataIndex.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                index.ResolveAll(batch, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                snapshot.Complete = false;
                snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                    $"Canonical metadata coverage is incomplete: {ex.Message}"];
                continue;
            }

            foreach (var id in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (snapshot.Fields[id] is not { } semantic || index.Cached(id) is not { } metadata)
                {
                    snapshot.Complete = false;
                    snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                        $"Canonical metadata coverage is incomplete: no descriptor was published for '{id}'."];
                    continue;
                }

                var updated = semantic with
                {
                    Label = metadata.Label,
                    Path = metadata.Path?.ToArray() ?? semantic.Path,
                    ReadOnly = metadata.Spec.ReadOnly,
                    CanonicalValueId = string.IsNullOrEmpty(metadata.CanonicalValueId) ? id : metadata.CanonicalValueId,
                    BindingKind = metadata.BindingKind,
                    Spec = FieldSpecMetadata.Snapshot(metadata.Spec),
                };
                var additional = SemanticSnapshot.Estimate(updated) - SemanticSnapshot.Estimate(semantic);
                if (additional > 0 && additional > maxBytes - snapshot.Bytes)
                {
                    snapshot.Complete = false;
                    snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                        $"Canonical metadata coverage is incomplete: review budget exceeded for '{id}'."];
                    continue;
                }

                snapshot.Bytes += additional;
                snapshot.Fields[id] = updated;
            }
        }
    }
}
