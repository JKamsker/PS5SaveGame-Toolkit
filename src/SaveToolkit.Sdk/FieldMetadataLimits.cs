namespace SaveToolkit.Sdk;

/// <summary>Shared bounds for targeted field discovery.</summary>
public static class FieldMetadataLimits
{
    /// <summary>Maximum requested IDs per resolution call, including duplicates.</summary>
    public const int MaxBatchSize = 200;
}
