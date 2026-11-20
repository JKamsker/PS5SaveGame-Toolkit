using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Separates bounded descriptor staging from same-identity snapshot replacement growth.</summary>
internal static class SemanticMetadataAdmission
{
    public static FieldMetadataIndex Create(ISaveDocument document, SemanticSnapshot snapshot, long maxBytes,
        CancellationToken cancellationToken, bool prioritize = false, IReadOnlySet<string>? protectedIds = null,
        bool presentationOnly = false)
    {
        if (document is not IFieldMetadataDocument) return new FieldMetadataIndex(document, maxBytes);
        // These are retained immutable fields, not an authoritative metadata cache. Credit belongs
        // only to the exact existing identity; aliases and new closure members cannot borrow it.
        var entry = new Dictionary<string, SemanticField>(snapshot.Fields, StringComparer.Ordinal);
        var entryBytes = snapshot.Bytes;
        SemanticSnapshot? reclamation = null;
        // Every admission revisits the whole retained closure, and batched callers admit it one
        // batch at a time. A retained descriptor is a detached snapshot and its entry is fixed, so
        // its growth is memoised per instance; only this resolution's fresh (plugin-owned)
        // descriptors are estimated again. Re-estimating everything per batch is quadratic.
        var retainedGrowth = new Dictionary<FieldMetadata, long>(ReferenceEqualityComparer.Instance);
        var entryCosts = new Dictionary<string, long>(StringComparer.Ordinal);
        return new FieldMetadataIndex(document, maxBytes, (descriptors, fresh) =>
        {
            reclamation = null;
            long growth = 0;
            foreach (var (id, metadata) in descriptors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long additional;
                if (fresh.Contains(id)) additional = Growth(id, metadata);
                else if (!retainedGrowth.TryGetValue(metadata, out additional))
                    retainedGrowth[metadata] = additional = Growth(id, metadata);
                if (additional > maxBytes - growth) throw TooLarge();
                growth += additional;
            }
            var reclaimed = entry.Values.Where(field => !snapshot.Fields.ContainsKey(field.ValueId))
                .Sum(field => entryCosts.TryGetValue(field.ValueId, out var cost) ? cost
                    : entryCosts[field.ValueId] = SemanticSnapshot.Estimate(field));
            var reservedTotal = entryBytes - reclaimed + growth;
            if (reservedTotal <= maxBytes) return;
            var remainingGrowth = Math.Max(0, reservedTotal - snapshot.Bytes);
            var staged = snapshot.Clone();
            if (!prioritize || !SemanticSnapshotPriority.MakeRoom(staged, remainingGrowth, maxBytes,
                    field => !field.MetadataPublished && field.Spec is not null
                        && !descriptors.ContainsKey(field.ValueId)
                        && protectedIds?.Contains(field.ValueId) != true, cancellationToken))
                throw TooLarge();
            reclamation = staged;
        }, () => { if (reclamation is not null) snapshot.ReplaceWith(reclamation); });

        long Growth(string id, FieldMetadata metadata)
        {
            if (!entry.TryGetValue(id, out var existing))
                return SemanticMetadataBudget.TryEstimate(metadata, maxBytes, out var added) ? added : throw TooLarge();
            var candidate = presentationOnly ? existing with { Presentation = metadata.Presentation }
                : Project(existing, metadata);
            var path = presentationOnly ? existing.Path : metadata.Path ?? existing.Path;
            if (!SemanticMetadataBudget.TryEstimate(candidate, path, maxBytes, out var replacement)
                || !SemanticSnapshot.TryEstimate(existing, maxBytes, out var previous))
                throw TooLarge();
            return Math.Max(0, replacement - previous);
        }
    }

    internal static SemanticField Project(SemanticField existing, FieldMetadata metadata) => existing with
    {
        Label = metadata.Label,
        ReadOnly = metadata.Spec.ReadOnly,
        CanonicalValueId = string.IsNullOrEmpty(metadata.CanonicalValueId) ? metadata.ValueId : metadata.CanonicalValueId,
        BindingKind = metadata.BindingKind,
        Spec = metadata.Spec,
        Presentation = metadata.Presentation,
        MetadataPublished = true,
    };

    private static InvalidOperationException TooLarge() =>
        new("Field metadata exceeds the bounded review budget.");
}
