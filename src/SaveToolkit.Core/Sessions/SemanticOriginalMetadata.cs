using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Enriches legacy semantic fields with explicitly published metadata.</summary>
internal static class SemanticOriginalMetadata
{
    public static bool HasMetadata(SemanticField field) => field.Spec is not null
        && !string.IsNullOrEmpty(field.CanonicalValueId);

    public static CanonicalMetadataOutcome Enrich(Func<ISaveDocument> openOriginal, SemanticSnapshot snapshot,
        long maxBytes, IReadOnlyList<string> retryIds, ref bool capturing, CancellationToken cancellationToken)
    {
        if ((retryIds.Count == 0 && snapshot.Fields.Values.All(HasMetadata)) || capturing)
            return new(true, []);
        capturing = true;
        try
        {
            using var original = openOriginal();
            if (original is IFieldMetadataDocument)
            {
                if (retryIds.Count > 0)
                {
                    var targeted = new SemanticTargetedCapture(maxBytes).Capture(
                        original, snapshot, retryIds, cancellationToken);
                    if (!targeted.Succeeded)
                        return new(false, targeted.IncompleteIds, targeted.Diagnostic);
                }

                return SemanticTargetedAuthority.ApplyCanonicalMetadata(original, snapshot, maxBytes, cancellationToken);
            }
            return new(true, []);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(false, [], $"Original canonical metadata coverage is incomplete: {ex.Message}");
        }
        finally { capturing = false; }
    }
}
