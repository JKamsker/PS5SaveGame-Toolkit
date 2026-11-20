using SaveToolkit.Core.References;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>Transport limits that every API host applies identically.</summary>
internal static class ApiLimits
{
    /// <summary>Request body and multipart limit for workspace uploads.</summary>
    public const long MaxUploadBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>Maximum form entries (text values plus files) in one multipart upload.</summary>
    public const int MaxFormValues = 100_000;

    /// <summary>Largest blob page returned by one read.</summary>
    public const int MaxBlobPage = 64 * 1024;

    private const long MultipartEnvelopeAllowanceBytes = 1024 * 1024;

    /// <summary>Default pack archive limit plus room for the multipart envelope; checked again against the store's limits.</summary>
    public static readonly long MaxReferencePackBody = new ReferencePackLimits().MaxArchiveBytes + MultipartEnvelopeAllowanceBytes;
}
