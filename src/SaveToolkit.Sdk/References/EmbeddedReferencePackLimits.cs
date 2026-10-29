namespace SaveToolkit.Sdk.References;

/// <summary>Per-pack guards for embedded resources, with the same defaults as the host loader.</summary>
/// <remarks>Every limit must be nonnegative. Zero permits no content in that category. Bounds apply per pack,
/// not across all packs in an assembly; MaxTotalBytes excludes manifests. Load rejects negative limits before discovery.</remarks>
public sealed record EmbeddedReferencePackLimits
{
    /// <summary>Sum of all listed dataset files, measured before text normalization.</summary>
    public long MaxTotalBytes { get; init; } = 128L * 1024 * 1024;

    /// <summary>Largest manifest in bytes.</summary>
    public long MaxManifestBytes { get; init; } = 256 * 1024;

    /// <summary>Largest individual records file in bytes.</summary>
    public long MaxRecordsFileBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>Largest individual image in bytes.</summary>
    public long MaxAssetBytes { get; init; } = 1024 * 1024;

    /// <summary>Maximum width and height of an image.</summary>
    public int MaxImageDimension { get; init; } = 2048;

    /// <summary>Maximum number of listed dataset files.</summary>
    public int MaxFiles { get; init; } = 20_000;

    /// <summary>Maximum records across all files of one catalog.</summary>
    public int MaxRecordsPerCatalog { get; init; } = 250_000;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxManifestBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRecordsFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxAssetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxImageDimension);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRecordsPerCatalog);
    }
}

/// <summary>An embedded pack failed validation; no partial context is returned.</summary>
public sealed class EmbeddedReferencePackException : Exception
{
    /// <summary>Creates a pack rejection with a diagnostic.</summary>
    public EmbeddedReferencePackException(string message) : base(message) { }
    /// <summary>Creates a pack rejection retaining its underlying failure.</summary>
    public EmbeddedReferencePackException(string message, Exception inner) : base(message, inner) { }
    /// <summary>Creates a pack rejection without a diagnostic.</summary>
    public EmbeddedReferencePackException() { }
}
