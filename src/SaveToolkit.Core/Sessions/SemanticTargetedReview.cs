using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Composes bounded targeted capture with review-specific failure authority.</summary>
internal static class SemanticTargetedReview
{
    public static TargetedCaptureOutcome Capture(SemanticTargetedCapture capture, ISaveDocument document, SemanticSnapshot snapshot,
        IReadOnlyList<string> valueIds, bool revokeOnFailure, Action<string, string> recordFailure,
        CancellationToken cancellationToken, IReadOnlySet<string>? protectedIds = null)
    {
        if (valueIds.Count == 0) return new(true, [], []);
        var result = capture.Capture(document, snapshot, valueIds, cancellationToken,
            markIncomplete: revokeOnFailure, protectedIds: protectedIds);
        if (result.IncompleteIds.Count > 0)
        {
            if (revokeOnFailure)
            {
                snapshot.OmittedCount = checked(snapshot.OmittedCount + result.IncompleteIds.Count);
                snapshot.OmittedCountIsLowerBound |= result.Diagnostic is not null;
            }
            foreach (var id in result.IncompleteIds)
            {
                if (revokeOnFailure) snapshot.Fields.Remove(id);
                else if (result.Diagnostic is { } incomplete) recordFailure(id, incomplete);
            }
            if (revokeOnFailure)
            {
                snapshot.Complete = false;
                if (result.Diagnostic is { } diagnostic)
                    snapshot.QueryWarnings = [.. snapshot.QueryWarnings, diagnostic];
            }
        }
        return new(result.Succeeded && result.IncompleteIds.Count == 0,
            result.CapturedIds, result.IncompleteIds);
    }
}

internal sealed record TargetedCaptureOutcome(bool Complete, IReadOnlyList<string> CapturedIds,
    IReadOnlyList<string> IncompleteIds);
