using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Copies optional published annotations onto already captured canonical identities.</summary>
internal static class SemanticPresentationProjection
{
    public static void Apply(ISaveDocument document, SemanticSnapshot snapshot, long maxBytes,
        CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument) return;
        var ids = snapshot.Fields.Values.Where(field => !field.MetadataPublished
                && field.CanonicalValueId is not null)
            .Select(field => field.ValueId).ToArray();
        if (ids.Length == 0) return;
        var working = snapshot.Clone();
        var index = new FieldMetadataIndex(document, Math.Max(0, maxBytes - working.Bytes));
        foreach (var batch in ids.Chunk(FieldMetadataIndex.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                index.ResolveAll(batch, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                snapshot.Complete = false;
                snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                    $"Presentation metadata coverage is incomplete for {Describe(batch)}: {ex.Message}"];
                snapshot.OmittedCount = checked(snapshot.OmittedCount + batch.Length);
                snapshot.OmittedCountIsLowerBound = true;
                return;
            }

            var missing = batch.Count(id => index.Cached(id) is null);
            if (missing > 0)
            {
                snapshot.Complete = false;
                snapshot.OmittedCount = checked(snapshot.OmittedCount + missing);
                snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                    $"Presentation metadata coverage omitted {missing} field(s) in {Describe(batch)}."];
                return;
            }

            foreach (var id in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (working.Fields.TryGetValue(id, out var field) && index.Cached(id) is { } metadata)
                {
                    var updated = field with { Presentation = metadata.Presentation, MetadataPublished = true };
                    if (!SemanticSnapshot.TryEstimate(updated, long.MaxValue, out var updatedCost)
                        || !SemanticSnapshot.TryEstimate(field, long.MaxValue, out var existingCost)
                        || updatedCost - existingCost > maxBytes - working.Bytes)
                    {
                        snapshot.Complete = false;
                        snapshot.OmittedCount = checked(snapshot.OmittedCount + 1);
                        snapshot.OmittedCountIsLowerBound = true;
                        snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                            $"Presentation metadata coverage exceeded the review budget for '{id}'."];
                        return;
                    }
                    working.Bytes += Math.Max(0, updatedCost - existingCost);
                    working.Fields[id] = updated;
                }
            }
        }
        snapshot.ReplaceWith(working);

        static string Describe(IReadOnlyList<string> ids)
        {
            const int sampleSize = 8;
            var sample = string.Join(", ", ids.Take(sampleSize).Select(id => $"'{id}'"));
            return ids.Count <= sampleSize ? $"field batch [{sample}]" : $"field batch [{sample}, …]";
        }
    }
}
