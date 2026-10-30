using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

/// <summary>Matches bounded snapshots while retaining every explicitly published projection.</summary>
internal static class SemanticSnapshotComparison
{
    public static IReadOnlyList<SemanticChange> Compare(IReadOnlyDictionary<string, SemanticField> original,
        bool originalComplete, IReadOnlyDictionary<string, SemanticField> current, bool currentComplete,
        ISaveDocument document, IReadOnlyDictionary<string, SemanticMemberContext> memberContext,
        CancellationToken cancellationToken)
    {
        var associations = CanonicalAssociations(original, current);
        var beforeGroups = Groups(original, associations);
        var afterGroups = Groups(current, associations);
        var changes = new List<SemanticChange>();
        foreach (var key in beforeGroups.Keys.Concat(afterGroups.Keys).Distinct(StringComparer.Ordinal))
        {
            beforeGroups.TryGetValue(key, out var before);
            afterGroups.TryGetValue(key, out var after);
            Add(key, before, after);
        }
        return changes;

        void Add(string key, SemanticFieldGroup? before, SemanticFieldGroup? after)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (before is null && !originalComplete || after is null && !currentComplete) return;
            var representative = after?.Representative ?? before!.Representative;
            var members = (before?.Members.Values ?? Enumerable.Empty<SemanticField>())
                .Concat(after?.Members.Values ?? Enumerable.Empty<SemanticField>());
            var changed = members.GroupBy(field => field.ValueId, StringComparer.Ordinal)
                .Where(group => ProjectionChanged(group.Key, before, after, originalComplete, currentComplete))
                .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            if (changed.Count == 0) return;

            var metadata = representative.MetadataPublished;
            var available = after is not null && (after.Representative.Path.Length > 0
                || after.Representative.Target is not null);
            var navigation = new SemanticNavigationTarget(after?.Representative.Path ?? before!.Representative.Path,
                after?.Representative.Target ?? before?.Representative.Target, available,
                available ? null : "The field is unavailable in the current reviewed view.");
            var aliases = members.GroupBy(field => field.ValueId, StringComparer.Ordinal)
                .Select(group => after?.Members.GetValueOrDefault(group.Key)
                    ?? group.OrderByDescending(field => field.MetadataPublished).First())
                .Where(field => field.ValueId != representative.ValueId && field.MetadataPublished)
                .Select(field => new SemanticProjection(field.ValueId, field.Label, field.Path,
                    field.BindingKind, field.Presentation, ContextFor(field, memberContext),
                    changed.Contains(field.ValueId)))
                .ToArray();
            var primaryChanged = changed.Contains(representative.ValueId)
                || representative.CanonicalValueId is { } canonical && changed.Contains(canonical);
            var canRevert = primaryChanged && before?.Members.ContainsKey(representative.ValueId) == true
                && after?.Members.GetValueOrDefault(representative.ValueId) is { ReadOnly: false } && document.CanEdit;
            changes.Add(new SemanticChange(representative.ValueId, representative.Label, representative.Path,
                before?.Representative.Value ?? FieldValue.Null, after?.Representative.Value ?? FieldValue.Null,
                before?.Members.ContainsKey(representative.ValueId) == true,
                after?.Members.ContainsKey(representative.ValueId) == true, canRevert)
            {
                Target = after?.Representative.Target,
                CanonicalValueId = metadata ? key : null,
                BindingKind = metadata ? representative.BindingKind : null,
                Presentation = representative.Presentation,
                Member = ContextFor(representative, memberContext),
                Aliases = aliases.Length == 0 ? null : aliases,
                NavigationTarget = metadata ? navigation : null,
                CanNavigate = metadata ? available : null,
                RevertReason = canRevert ? null : primaryChanged ? null
                    : "A read-only projection changed without a validated primary transition."
            });
        }
    }

    private static bool ProjectionChanged(string id, SemanticFieldGroup? before, SemanticFieldGroup? after,
        bool originalComplete, bool currentComplete)
    {
        var oldField = before?.Members.GetValueOrDefault(id);
        var newField = after?.Members.GetValueOrDefault(id);
        if (oldField is null || newField is null) return originalComplete && currentComplete;
        return oldField.Value != newField.Value;
    }

    private static Dictionary<string, SemanticFieldGroup> Groups(IReadOnlyDictionary<string, SemanticField> fields,
        IReadOnlyDictionary<string, string> associations)
    {
        var groups = new Dictionary<string, SemanticFieldGroup>(StringComparer.Ordinal);
        foreach (var (id, field) in fields)
        {
            var key = field.MetadataPublished && !string.IsNullOrEmpty(field.CanonicalValueId)
                ? field.CanonicalValueId! : associations.GetValueOrDefault(id, id);
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = new(key);
            group.Members[id] = field;
            if (group.Representative is null || Prefer(field, group.Representative)) group.Representative = field;
        }
        return groups;
    }

    private static IReadOnlyDictionary<string, string> CanonicalAssociations(
        IReadOnlyDictionary<string, SemanticField> original, IReadOnlyDictionary<string, SemanticField> current)
    {
        var candidates = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var field in original.Values.Concat(current.Values).Where(field =>
                     field.MetadataPublished && !string.IsNullOrEmpty(field.CanonicalValueId)))
        {
            if (!candidates.TryGetValue(field.ValueId, out var ids)) candidates[field.ValueId] = ids = [];
            ids.Add(field.CanonicalValueId!);
        }
        return candidates.Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Single(), StringComparer.Ordinal);
    }

    private static bool Prefer(SemanticField candidate, SemanticField existing) =>
        (candidate.BindingKind == FieldBindingKind.Primary && existing.BindingKind != FieldBindingKind.Primary)
        || (candidate.BindingKind == existing.BindingKind && candidate.ValueId == candidate.CanonicalValueId
            && existing.ValueId != existing.CanonicalValueId);

    private static SemanticMemberContext? ContextFor(SemanticField field,
        IReadOnlyDictionary<string, SemanticMemberContext> contexts) =>
        field.CanonicalValueId is { } canonical && contexts.TryGetValue(canonical, out var context)
            ? context : contexts.GetValueOrDefault(field.ValueId);

    private sealed class SemanticFieldGroup(string key)
    {
        public string Key { get; } = key;
        public Dictionary<string, SemanticField> Members { get; } = new(StringComparer.Ordinal);
        public SemanticField Representative { get; set; } = null!;
    }
}
