using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

internal sealed record TargetedCaptureResult(bool Succeeded, IReadOnlyList<string> CapturedIds,
    IReadOnlyList<string> IncompleteIds, string? Diagnostic = null);

/// <summary>Stages bounded metadata values before publishing them into a semantic snapshot.</summary>
internal sealed class SemanticTargetedCapture(long maxBytes)
{
    public TargetedCaptureResult Capture(ISaveDocument document, SemanticSnapshot snapshot,
        IReadOnlyList<string> valueIds, CancellationToken cancellationToken, bool markIncomplete = true)
    {
        if (valueIds.Count == 0) return new(true, [], []);
        if (document is not IFieldMetadataDocument)
            return Failure("Targeted metadata is unavailable for this document.", valueIds);

        var working = snapshot.Clone();
        var captured = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            CaptureCore(document, working, valueIds, captured, markIncomplete, cancellationToken);
            snapshot.ReplaceWith(working);
            var incomplete = valueIds.Where(id => !captured.Contains(id)).ToArray();
            return new(true, captured.ToArray(), incomplete,
                incomplete.Length == 0 ? null : "Targeted metadata coverage is incomplete for one or more affected IDs.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failure(SemanticDiagnostics.Exception("Targeted metadata coverage is incomplete", ex), valueIds);
        }

        TargetedCaptureResult Failure(string message, IReadOnlyList<string> ids) => new(false, [], ids, message);
    }

    private void CaptureCore(ISaveDocument document, SemanticSnapshot snapshot, IReadOnlyList<string> ids,
        ISet<string> captured, bool markIncomplete, CancellationToken cancellationToken)
    {
        var index = new FieldMetadataIndex(document, Math.Max(0, maxBytes - snapshot.Bytes));
        for (var offset = 0; offset < ids.Count && snapshot.Bytes < maxBytes; offset += FieldMetadataLimits.MaxBatchSize)
        {
            var batch = ids.Skip(offset).Take(FieldMetadataLimits.MaxBatchSize).ToArray();
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
                    var candidate = existing with
                    {
                        ReadOnly = field.Spec.ReadOnly, CanonicalValueId = canonical, BindingKind = field.BindingKind,
                        Spec = field.Spec, Presentation = field.Presentation, Label = field.Label,
                        Path = field.Path?.ToArray() ?? existing.Path,
                    };
                    if (!SemanticSnapshot.TryEstimate(candidate, long.MaxValue, out var candidateCost)
                        || !SemanticSnapshot.TryEstimate(existing, long.MaxValue, out var existingCost)
                        || candidateCost - existingCost > maxBytes - snapshot.Bytes)
                    {
                        if (markIncomplete) snapshot.Complete = false;
                        continue;
                    }

                    snapshot.Bytes += Math.Max(0, candidateCost - existingCost);
                    snapshot.Fields[field.ValueId] = candidate with { Spec = FieldSpecMetadata.Snapshot(field.Spec) };
                    captured.Add(field.ValueId);
                    continue;
                }

                var semantic = new SemanticField(field.ValueId, field.Label, field.Path?.ToArray() ?? [],
                    document.GetValue(field.ValueId), field.Spec.ReadOnly,
                    CanonicalValueId: canonical, BindingKind: field.BindingKind,
                    Spec: field.Spec, Presentation: field.Presentation);
                if (!SemanticSnapshot.TryEstimate(semantic, maxBytes - snapshot.Bytes, out var cost))
                {
                    if (markIncomplete) snapshot.Complete = false;
                    continue;
                }

                snapshot.Bytes += cost;
                snapshot.Fields.Add(field.ValueId, semantic with { Spec = FieldSpecMetadata.Snapshot(field.Spec) });
                captured.Add(field.ValueId);
            }

            if (markIncomplete && seen.Count != requested.Count) snapshot.Complete = false;
        }
    }
}
