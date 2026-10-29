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
            index.ResolveAll(batch, cancellationToken);
            foreach (var id in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (snapshot.Fields[id] is { } field && index.Cached(id) is { } metadata)
                    snapshot.Fields[id] = field with { Presentation = metadata.Presentation, MetadataPublished = true };
            }
        }
    }
}
