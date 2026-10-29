using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Enriches legacy semantic fields with explicitly published metadata.</summary>
internal static class SemanticOriginalMetadata
{
    public static bool HasMetadata(SemanticField field) => field.Spec is not null
        && !string.IsNullOrEmpty(field.CanonicalValueId);

    public static void Enrich(Func<ISaveDocument> openOriginal, SemanticSnapshot snapshot,
        long maxBytes, ref bool capturing, CancellationToken cancellationToken)
    {
        if (snapshot.Fields.Values.All(HasMetadata) || capturing) return;
        capturing = true;
        try
        {
            using var original = openOriginal();
            if (original is IFieldMetadataDocument)
                SemanticTargetedAuthority.ApplyCanonicalMetadata(original, snapshot, maxBytes, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            snapshot.Complete = false;
            snapshot.QueryWarnings = [.. snapshot.QueryWarnings,
                $"Original canonical metadata coverage is incomplete: {ex.Message}"];
        }
        finally { capturing = false; }
    }
}
