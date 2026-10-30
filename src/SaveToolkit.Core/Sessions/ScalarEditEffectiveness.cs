using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

/// <summary>Proves a repeated scalar assignment ineffective without bypassing plugin validation/setters.</summary>
internal static class ScalarEditEffectiveness
{
    // An optimization limit, not an edit/export limit. Larger documents keep the normal mutation path.
    private const long MaxProofBytes = 8L * 1024 * 1024;

    public static IReadOnlyList<FileOutput>? Capture(ISaveDocument document, IReadOnlyList<ValueEdit> edits,
        CancellationToken cancellationToken)
    {
        try
        {
            if (edits.Count == 0 || edits.Any(edit => edit.Value.Kind == FieldValueKind.Union
                || document.GetValue(edit.ValueId) != edit.Value)) return null;
            return DocumentOutputSnapshot.TryCapture(document, MaxProofBytes, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    public static EditOutcome Complete(ISaveDocument document, IReadOnlyList<ValueEdit> edits,
        IReadOnlyList<FileOutput>? before, EditOutcome outcome)
    {
        if (before is null || !outcome.Succeeded || outcome.StructureChanged || outcome.Revert is not null
            || outcome.Changes.Count == 0) return outcome;

        try
        {
            // Build membership once: IDs are ordinal and values use canonical FieldValue equality.
            // Multiple requested representations for an ID retain the original membership semantics.
            var assignments = new Dictionary<string, HashSet<FieldValue>>(StringComparer.Ordinal);
            foreach (var edit in edits)
            {
                if (!assignments.TryGetValue(edit.ValueId, out var values))
                    assignments.Add(edit.ValueId, values = []);
                values.Add(edit.Value);
            }
            // Derived rows and mixed effective batches retain their original semantics.
            if (outcome.Changes.Any(change => change.OldValue != change.NewValue
                || !assignments.TryGetValue(change.ValueId, out var values) || !values.Contains(change.NewValue))) return outcome;
            var after = DocumentOutputSnapshot.TryCapture(document, MaxProofBytes, CancellationToken.None);
            if (after is null || before.Count != after.Count) return outcome;
            for (var index = 0; index < before.Count; index++)
                if (before[index].RelativePath != after[index].RelativePath
                    || !before[index].Content.Span.SequenceEqual(after[index].Content.Span)) return outcome;
            return outcome with { Changes = [] };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return outcome; }
    }
}
