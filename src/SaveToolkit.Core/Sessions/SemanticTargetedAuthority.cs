using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

internal sealed record CanonicalMetadataOutcome(bool Applied, IReadOnlyList<string> IncompleteIds,
    string? Diagnostic = null, IReadOnlyList<string>? RecoveredIds = null)
{
    public IReadOnlyList<string> Recovered => RecoveredIds ?? [];
}

internal static class SemanticTargetedAuthority
{
    public static IReadOnlyList<string> Validate(SemanticSnapshot original, SemanticSnapshot current,
        ReferenceFieldService references, IReadOnlySet<string>? retainRejectedIds = null)
    {
        var rejected = new List<string>();
        foreach (var (id, before) in original.Fields)
        {
            if (before.CanonicalValueId is null || !current.Fields.TryGetValue(id, out var after)) continue;
            if (string.Equals(before.CanonicalValueId, after.CanonicalValueId, StringComparison.Ordinal)
                && before.BindingKind == after.BindingKind && before.ReadOnly == after.ReadOnly
                && before.Spec is { } beforeSpec && after.Spec is { } afterSpec
                && AuthoritySpecAgree(beforeSpec, afterSpec)
                && OriginalValueMatchesCurrentPolicy(before.Value, afterSpec, references)) continue;
            if (retainRejectedIds?.Contains(id) == true && current.Fields.TryGetValue(id, out var rejectedField))
            {
                // Keep the observed value/existence for an honest incomplete row, but make the
                // internal comparison conservative so a rejected policy cannot become revertible.
                current.Fields[id] = rejectedField with { ReadOnly = true };
            }
            else current.Fields.Remove(id);
            current.Complete = false;
            rejected.Add(id);
            current.QueryWarnings = [.. current.QueryWarnings,
                SemanticDiagnostics.WithIdentity(
                    "Targeted metadata coverage is incomplete: authority changed for '",
                    id, "'; revert is unavailable.")];
        }

        return rejected;
    }

    private static bool OriginalValueMatchesCurrentPolicy(FieldValue value, FieldSpec spec,
        ReferenceFieldService references) => FieldValueCoercer.Normalize(value, spec,
            allowReadOnly: true, references).Ok;

    public static CanonicalMetadataOutcome ApplyCanonicalMetadata(ISaveDocument document, SemanticSnapshot snapshot,
        long maxBytes, CancellationToken cancellationToken)
    {
        if (document is not IFieldMetadataDocument || snapshot.Fields.Count == 0)
            return new(true, []);

        var working = snapshot.Clone();
        var index = new FieldMetadataIndex(document, Math.Max(0, maxBytes - working.Bytes));
        // Targeted capture already admitted these descriptors, including their current
        // presentation. Re-resolving them here would spend the same review budget twice and
        // could replace a current annotation with an older fallback. Only legacy fields need
        // canonical authority enrichment.
        var ids = working.Fields.Values.Where(field => !field.MetadataPublished)
            .Select(field => field.ValueId).ToArray();
        foreach (var batch in ids.Chunk(FieldMetadataIndex.BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                index.ResolveAll(batch, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                return Failure(batch, SemanticDiagnostics.Exception("Canonical metadata coverage is incomplete", ex));
            }

            foreach (var id in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (working.Fields[id] is not { } semantic || index.Cached(id) is not { } metadata)
                    return Failure([id], SemanticDiagnostics.WithIdentity(
                        "Canonical metadata coverage is incomplete: no descriptor was published for '", id, "'."));

                var updated = semantic with
                {
                    Label = metadata.Label,
                    Path = metadata.Path?.ToArray() ?? semantic.Path,
                    ReadOnly = metadata.Spec.ReadOnly,
                    CanonicalValueId = string.IsNullOrEmpty(metadata.CanonicalValueId) ? id : metadata.CanonicalValueId,
                    BindingKind = metadata.BindingKind,
                    Spec = metadata.Spec,
                    Presentation = metadata.Presentation, MetadataPublished = true,
                };
                if (!SemanticSnapshot.TryEstimate(updated, long.MaxValue, out var updatedCost)
                    || !SemanticSnapshot.TryEstimate(semantic, long.MaxValue, out var existingCost))
                    return Failure([id], SemanticDiagnostics.WithIdentity(
                        "Canonical metadata coverage is incomplete: review metadata is too large for '", id, "'."));

                var additional = updatedCost - existingCost;
                if (additional > 0 && additional > maxBytes - working.Bytes)
                    return Failure([id], SemanticDiagnostics.WithIdentity(
                        "Canonical metadata coverage is incomplete: review budget exceeded for '", id, "'."));

                working.Bytes += Math.Max(0, additional);
                working.Fields[id] = updated with { Spec = FieldSpecMetadata.Snapshot(metadata.Spec) };
            }
        }

        snapshot.ReplaceWith(working);
        return new(true, []);

        CanonicalMetadataOutcome Failure(IReadOnlyList<string> incompleteIds, string diagnostic) =>
            new(false, incompleteIds, diagnostic);
    }

    private static bool AuthoritySpecAgree(FieldSpec first, FieldSpec second)
    {
        if (FieldSpecMetadata.Agree(first, second)) return true;
        if (first.GetType() != second.GetType()
            || !JsonSerializer.SerializeToUtf8Bytes(first).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(second)))
            return false;

        return (first, second) switch
        {
            (ReferenceSpec a, ReferenceSpec b) => PoliciesEquivalent(a.Assess, b.Assess),
            (NullableSpec a, NullableSpec b) => AuthoritySpecAgree(a.Inner, b.Inner),
            (UnionSpec a, UnionSpec b) => a.Options.Count == b.Options.Count
                && a.Options.Zip(b.Options).All(pair => AuthoritySpecAgree(pair.First.Spec, pair.Second.Spec)),
            _ => true,
        };
    }

    private static bool PoliciesEquivalent(Func<ReferenceRecord, ReferenceEligibility>? first,
        Func<ReferenceRecord, ReferenceEligibility>? second)
    {
        if (first == second) return true;
        return first is not null && second is not null
            && first.Method == second.Method
            && first.Target?.GetType() == second.Target?.GetType();
    }
}
