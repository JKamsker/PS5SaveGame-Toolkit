using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

internal sealed record TargetedCaptureResult(bool Succeeded, string? Diagnostic = null);

/// <summary>Stages bounded metadata values before publishing them into a semantic snapshot.</summary>
internal sealed class SemanticTargetedCapture(long maxBytes)
{
    public TargetedCaptureResult Capture(ISaveDocument document, SemanticSnapshot snapshot,
        IEnumerable<string> valueIds, CancellationToken cancellationToken)
    {
        var ids = valueIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return new(true);
        if (document is not IFieldMetadataDocument)
            return Failure("Targeted metadata is unavailable for this document.");

        var working = snapshot.Clone();
        try
        {
            CaptureCore(document, working, ids, cancellationToken);
            snapshot.ReplaceWith(working);
            return new(true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failure($"Targeted metadata coverage is incomplete: {ex.Message}");
        }

        TargetedCaptureResult Failure(string message) => new(false, message);
    }

    private void CaptureCore(ISaveDocument document, SemanticSnapshot snapshot, IReadOnlyList<string> ids,
        CancellationToken cancellationToken)
    {
        var index = new FieldMetadataIndex(document);
        foreach (var batch in ids.Chunk(FieldMetadataLimits.MaxBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = batch.ToHashSet(StringComparer.Ordinal);
            index.ResolveAll(batch, cancellationToken);
            var descriptors = batch.Select(index.Cached).Where(field => field is not null).Cast<FieldMetadata>().ToArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in descriptors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!requested.Contains(field.ValueId) || !seen.Add(field.ValueId) || string.IsNullOrEmpty(field.Label)
                    || field.Spec is null || !Enum.IsDefined(field.BindingKind))
                    throw new InvalidOperationException("Invalid targeted field metadata.");

                var canonical = string.IsNullOrEmpty(field.CanonicalValueId) ? field.ValueId : field.CanonicalValueId;
                if (snapshot.Fields.TryGetValue(field.ValueId, out var existing))
                {
                    snapshot.Fields[field.ValueId] = existing with
                    {
                        ReadOnly = field.Spec.ReadOnly, CanonicalValueId = canonical, BindingKind = field.BindingKind,
                        Spec = FieldSpecMetadata.Snapshot(field.Spec), Label = field.Label,
                        Path = field.Path?.ToArray() ?? existing.Path,
                    };
                    continue;
                }

                var semantic = new SemanticField(field.ValueId, field.Label, field.Path?.ToArray() ?? [],
                    document.GetValue(field.ValueId), field.Spec.ReadOnly,
                    CanonicalValueId: canonical, BindingKind: field.BindingKind,
                    Spec: FieldSpecMetadata.Snapshot(field.Spec));
                var cost = SemanticSnapshot.Estimate(semantic);
                if (cost > maxBytes - snapshot.Bytes)
                {
                    snapshot.Complete = false;
                    continue;
                }

                snapshot.Bytes += cost;
                snapshot.Fields.Add(field.ValueId, semantic);
            }

            if (seen.Count != requested.Count) snapshot.Complete = false;
        }
    }
}
