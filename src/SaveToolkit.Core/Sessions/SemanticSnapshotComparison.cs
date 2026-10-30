using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

/// <summary>Matches bounded snapshots without allocating another set of every field identity.</summary>
internal static class SemanticSnapshotComparison
{
    public static IReadOnlyList<SemanticChange> Compare(IReadOnlyDictionary<string, SemanticField> original, bool originalComplete,
        IReadOnlyDictionary<string, SemanticField> current, bool currentComplete, ISaveDocument document, CancellationToken cancellationToken)
    {
        var changes = new List<SemanticChange>();
        if (!HasCanonicalIdentity(original) && !HasCanonicalIdentity(current))
        {
            foreach (var (id, before) in original)
            {
                current.TryGetValue(id, out var after);
                Add(id, before, after);
            }

            foreach (var (id, after) in current)
                if (!original.ContainsKey(id)) Add(id, null, after);
            return changes;
        }

        // Explicit canonical metadata may expose one writable primary through several read-only
        // projections. Compare one representative per canonical identity so review and replay
        // cannot duplicate the same storage transition. No ID inference occurs here.
        var beforeGroups = Groups(original);
        var afterGroups = Groups(current);
        foreach (var key in beforeGroups.Keys.Concat(afterGroups.Keys).Distinct(StringComparer.Ordinal))
        {
            beforeGroups.TryGetValue(key, out var before);
            afterGroups.TryGetValue(key, out var after);
            Add(key, before, after);
        }
        return changes;

        static bool HasCanonicalIdentity(IReadOnlyDictionary<string, SemanticField> fields) =>
            fields.Values.Any(field => !string.IsNullOrEmpty(field.CanonicalValueId));

        static Dictionary<string, SemanticField> Groups(IReadOnlyDictionary<string, SemanticField> fields)
        {
            var groups = new Dictionary<string, SemanticField>(StringComparer.Ordinal);
            foreach (var (id, field) in fields)
            {
                var key = string.IsNullOrEmpty(field.CanonicalValueId) ? id : field.CanonicalValueId;
                if (!groups.TryGetValue(key, out var existing) || Prefer(field, existing)) groups[key] = field;
            }
            return groups;
        }

        static bool Prefer(SemanticField candidate, SemanticField existing) =>
            (candidate.BindingKind == FieldBindingKind.Primary && existing.BindingKind != FieldBindingKind.Primary)
            || (candidate.BindingKind == existing.BindingKind && candidate.ValueId == candidate.CanonicalValueId
                && existing.ValueId != existing.CanonicalValueId);

        void Add(string id, SemanticField? before, SemanticField? after)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((before is null && !originalComplete) || (after is null && !currentComplete)
                || (before is not null && after is not null && before.Value == after.Value)) return;

            var field = after ?? before!;
            changes.Add(new SemanticChange(field.ValueId, field.Label, field.Path,
                before?.Value ?? FieldValue.Null, after?.Value ?? FieldValue.Null,
                before is not null, after is not null, before is not null && after is { ReadOnly: false } && document.CanEdit)
            { Target = after?.Target });
        }
    }
}
