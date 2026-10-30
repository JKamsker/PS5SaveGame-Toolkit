namespace SaveToolkit.Core.Sessions;

/// <summary>Charges actual changed-row context before retaining optional unchanged view evidence.</summary>
internal static class SemanticContextAdmission
{
    public static MemberContextResolution Resolve(
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, SemanticMemberContext>>? resolver,
        IReadOnlyList<SemanticChange> changes, SemanticSnapshot original, SemanticSnapshot current,
        long maxBytes, CancellationToken cancellationToken, IReadOnlySet<string>? targeted = null,
        IReadOnlySet<string>? reported = null)
    {
        var usedBytes = Math.Max(original.Bytes, current.Bytes);
        if (resolver is null || changes.All(change => change.CanonicalValueId is null))
            return SemanticMemberContextResolver.Resolve(resolver, changes, usedBytes, maxBytes, cancellationToken);
        // Stage the actual bounded response once; no invented allowance or repeated plugin callback.
        var candidate = SemanticMemberContextResolver.Resolve(resolver, changes, 0, maxBytes, cancellationToken);
        if (candidate.AcceptedBytes <= maxBytes - usedBytes) return candidate;
        var before = original.Clone();
        var after = current.Clone();
        var protectedIds = changes.Select(change => change.ValueId)
            .Concat(changes.SelectMany(change => change.Aliases?.Select(alias => alias.ValueId) ?? []))
            .ToHashSet(StringComparer.Ordinal);
        bool Optional(SemanticField field) => !protectedIds.Contains(field.ValueId)
            && targeted?.Contains(field.ValueId) != true && reported?.Contains(field.ValueId) != true
            && original.Fields.TryGetValue(field.ValueId, out var oldField)
            && current.Fields.TryGetValue(field.ValueId, out var newField)
            && oldField.Value == newField.Value;
        if (SemanticSnapshotPriority.MakeRoom(before, candidate.AcceptedBytes, maxBytes, Optional, cancellationToken)
            && SemanticSnapshotPriority.MakeRoom(after, candidate.AcceptedBytes, maxBytes, Optional, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            original.ReplaceWith(before);
            current.ReplaceWith(after);
            return candidate;
        }
        var bounded = SemanticMemberContextResolver.Resolve(_ => candidate.Contexts, changes,
            usedBytes, maxBytes, cancellationToken);
        // This pass counts every missing identity, including those absent from the staged response.
        return bounded with { Complete = candidate.Complete && bounded.Complete,
            Diagnostic = candidate.Diagnostic ?? bounded.Diagnostic };
    }
}
