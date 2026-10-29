using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

/// <summary>Matches bounded snapshots without allocating another set of every field identity.</summary>
internal static class SemanticSnapshotComparison
{
    public static IReadOnlyList<SemanticChange> Compare(IReadOnlyDictionary<string, SemanticField> original, bool originalComplete,
        IReadOnlyDictionary<string, SemanticField> current, bool currentComplete, ISaveDocument document,
        IReadOnlyDictionary<string, SemanticMemberContext> memberContext, CancellationToken cancellationToken)
    {
        var changes = new List<SemanticChange>();
        // Explicit canonical metadata may expose one writable primary through several read-only
        // projections. Compare one representative per canonical identity so review and replay
        // cannot duplicate the same storage transition. No ID inference occurs here.
        var beforeGroups = Groups(original);
        var afterGroups = Groups(current);
        foreach (var key in beforeGroups.Keys.Concat(afterGroups.Keys).Distinct(StringComparer.Ordinal))
        {
            beforeGroups.TryGetValue(key, out var before);
            afterGroups.TryGetValue(key, out var after);
            Add(key, before, after, original, current);
        }
        return changes;

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

        void Add(string id, SemanticField? before, SemanticField? after,
            IReadOnlyDictionary<string, SemanticField> beforeFields, IReadOnlyDictionary<string, SemanticField> afterFields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((before is null && !originalComplete) || (after is null && !currentComplete)
                || (before is not null && after is not null && before.Value == after.Value)) return;

            var field = after ?? before!;
            var canonical = field.MetadataPublished ? id : null;
            var canNavigate = after is not null && (after.Path.Length > 0 || after.Target is not null);
            var navigation = new SemanticNavigationTarget(after?.Path ?? before?.Path ?? [], after?.Target ?? before?.Target,
                canNavigate, canNavigate ? null : "The field is unavailable in the current reviewed view.");
            var aliases = beforeFields.Values.Concat(afterFields.Values)
                .Where(candidate => candidate.MetadataPublished
                    && (string.IsNullOrEmpty(candidate.CanonicalValueId) ? candidate.ValueId : candidate.CanonicalValueId) == id)
                .GroupBy(candidate => candidate.ValueId, StringComparer.Ordinal)
                .Select(group => group.First())
                .Where(alias => alias.MetadataPublished && alias.ValueId != field.ValueId)
                .Select(alias => new SemanticProjection(alias.ValueId, alias.Label, alias.Path,
                    alias.BindingKind, alias.Presentation, memberContext.GetValueOrDefault(alias.ValueId))).ToArray();
            var canRevert = before is not null && after is { ReadOnly: false } && document.CanEdit;
            changes.Add(new SemanticChange(field.ValueId, field.Label, field.Path,
                before?.Value ?? FieldValue.Null, after?.Value ?? FieldValue.Null,
                before is not null, after is not null, canRevert)
            {
                // The legacy collection target is only valid for a current row. Historical
                // paths remain available through NavigationTarget with an explicit unavailable state.
                Target = after?.Target,
                CanonicalValueId = canonical,
                BindingKind = field.MetadataPublished ? field.BindingKind : null,
                Presentation = field.Presentation,
                Member = memberContext.GetValueOrDefault(field.ValueId),
                Aliases = aliases.Length == 0 ? null : aliases,
                NavigationTarget = field.MetadataPublished ? navigation : null,
                CanNavigate = field.MetadataPublished ? canNavigate : null,
                RevertReason = canRevert ? null : before is null || after is null
                    ? "This field was added or removed; use Undo for structural changes."
                    : field.BindingKind == FieldBindingKind.Derived || field.ReadOnly
                        ? "This projection is read-only and updates when its canonical primary changes."
                        : document.CanEdit ? "The server could not validate a safe revert for this field."
                            : "The document is read-only."
            });
        }
    }
}
