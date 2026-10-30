using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Enriches legacy semantic fields with explicitly published metadata.</summary>
internal static class SemanticOriginalMetadata
{
    public static bool HasMetadata(SemanticField field) => field.Spec is not null
        && !string.IsNullOrEmpty(field.CanonicalValueId);

    public static CanonicalMetadataOutcome Enrich(Func<ISaveDocument> openOriginal, SemanticSnapshot snapshot,
        long maxBytes, IReadOnlyList<string> retryIds, bool forceAttempt, ref bool capturing,
        CancellationToken cancellationToken, IReadOnlySet<string>? protectedIds = null)
    {
        if ((!forceAttempt && retryIds.Count == 0 && snapshot.Fields.Values.All(HasMetadata)) || capturing)
            return new(true, []);
        capturing = true;
        try
        {
            using var original = openOriginal();
            if (original is IFieldMetadataDocument)
            {
                var recovered = new List<string>();
                if (retryIds.Count > 0)
                {
                    var targeted = new SemanticTargetedCapture(maxBytes).Capture(
                        original, snapshot, retryIds, cancellationToken, markIncomplete: false,
                        protectedIds: protectedIds);
                    recovered.AddRange(targeted.CapturedIds);
                    if (targeted.IncompleteIds.Count > 0)
                        return new(false, targeted.IncompleteIds, targeted.Diagnostic, recovered);
                }

                var outcome = SemanticTargetedAuthority.ApplyCanonicalMetadata(original, snapshot, maxBytes, cancellationToken);
                return outcome with { RecoveredIds = recovered };
            }
            return new(false, [], "Original canonical metadata is unavailable for the retry document.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(false, [], SemanticDiagnostics.Exception("Original canonical metadata coverage is incomplete", ex));
        }
        finally { capturing = false; }
    }
}
