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
        // Preserve the union's original-first ordering, including removals followed by additions.
        foreach (var (id, before) in original)
            Add(id, before, current.GetValueOrDefault(id));
        foreach (var (id, after) in current)
            if (!original.ContainsKey(id)) Add(id, null, after);
        return changes;

        void Add(string id, SemanticField? before, SemanticField? after)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((before is null && !originalComplete) || (after is null && !currentComplete)
                || (before is not null && after is not null && before.Value == after.Value)) return;

            var field = after ?? before!;
            changes.Add(new SemanticChange(id, field.Label, field.Path,
                before?.Value ?? FieldValue.Null, after?.Value ?? FieldValue.Null,
                before is not null, after is not null, before is not null && after is { ReadOnly: false } && document.CanEdit)
            { Target = after?.Target });
        }
    }
}
