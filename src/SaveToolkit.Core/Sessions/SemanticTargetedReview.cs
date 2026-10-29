using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Composes bounded targeted capture with review-specific failure authority.</summary>
internal static class SemanticTargetedReview
{
    public static bool Capture(SemanticTargetedCapture capture, ISaveDocument document, SemanticSnapshot snapshot,
        IEnumerable<string> valueIds, bool revokeOnFailure, Action<string, string> recordFailure,
        CancellationToken cancellationToken)
    {
        var ids = valueIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return true;
        var result = capture.Capture(document, snapshot, ids, cancellationToken);
        if (result.Succeeded) return true;
        if (revokeOnFailure)
        {
            foreach (var id in ids) snapshot.Fields.Remove(id);
            snapshot.Complete = false;
            snapshot.QueryWarnings = [.. snapshot.QueryWarnings, result.Diagnostic!];
        }
        else if (result.Diagnostic is { } diagnostic)
        {
            foreach (var id in ids) recordFailure(id, diagnostic);
        }
        return false;
    }
}
