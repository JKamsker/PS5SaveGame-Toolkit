using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Copies optional published annotations onto already captured canonical identities.</summary>
internal static class SemanticPresentationProjection
{
    public static void Apply(ISaveDocument document, SemanticSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument) return;
        var ids = snapshot.Fields.Values.Where(field => field.CanonicalValueId is not null)
            .Select(field => field.ValueId).ToArray();
        if (ids.Length == 0) return;
        var index = new FieldMetadataIndex(document);
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
                snapshot.OmittedCount += batch.Length;
                snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                    $"Presentation metadata coverage is incomplete for {Describe(batch)}: {ex.Message}"];
                continue;
            }

            var missing = batch.Count(id => index.Cached(id) is null);
            if (missing > 0)
            {
                snapshot.Complete = false;
                snapshot.OmittedCount += missing;
                snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                    $"Presentation metadata coverage omitted {missing} field(s) in {Describe(batch)}."];
            }

            foreach (var id in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (snapshot.Fields.TryGetValue(id, out var field) && index.Cached(id) is { } metadata)
                    snapshot.Fields[id] = field with { Presentation = metadata.Presentation, MetadataPublished = true };
            }
        }

        static string Describe(IReadOnlyList<string> ids)
        {
            const int sampleSize = 8;
            var sample = string.Join(", ", ids.Take(sampleSize).Select(id => $"'{id}'"));
            return ids.Count <= sampleSize ? $"field batch [{sample}]" : $"field batch [{sample}, …]";
        }
    }
}
