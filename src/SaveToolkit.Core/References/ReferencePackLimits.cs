namespace SaveToolkit.Core.References;

/// <summary>Size guards applied to every pack, embedded or user-supplied.</summary>
public sealed record ReferencePackLimits
{
    /// <summary>Largest pack archive accepted for import.</summary>
    public long MaxArchiveBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Sum of all listed files after decompression.</summary>
    public long MaxTotalBytes { get; init; } = 128L * 1024 * 1024;

    public long MaxManifestBytes { get; init; } = 256 * 1024;

    public long MaxRecordsFileBytes { get; init; } = 32L * 1024 * 1024;

    public long MaxAssetBytes { get; init; } = 1024 * 1024;

    /// <summary>Width and height cap for images (decompression-bomb guard for the browser).</summary>
    public int MaxImageDimension { get; init; } = 2048;

    public int MaxFiles { get; init; } = 20_000;

    public int MaxRecordsPerCatalog { get; init; } = 250_000;
}

/// <summary>A pack failed validation. The message is shown to the user.</summary>
public sealed class ReferencePackException : Exception
{
    public ReferencePackException(string message) : base(message) { }
    public ReferencePackException(string message, Exception inner) : base(message, inner) { }
    public ReferencePackException() { }
}
