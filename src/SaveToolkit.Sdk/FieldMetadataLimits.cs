namespace SaveToolkit.Sdk;

/// <summary>Shared bounds for targeted field discovery.</summary>
public static class FieldMetadataLimits
{
    /// <summary>Maximum requested IDs per resolution call, including duplicates.</summary>
    public const int MaxBatchSize = 200;

    /// <summary>Maximum retained targeted IDs, including canonical closure, per bounded operation.</summary>
    public const int MaxClosureIds = 4096;

    /// <summary>Maximum nested field-spec depth accepted by immutable metadata snapshots.</summary>
    public const int MaxSpecDepth = 128;
}
