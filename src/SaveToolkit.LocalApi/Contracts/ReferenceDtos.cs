using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Api.Contracts;

/// <summary>Every loaded pack plus packs that were found but rejected.</summary>
public sealed record ReferencePacksDto(IReadOnlyList<ReferencePackInfo> Packs, IReadOnlyList<ReferencePackLoadError> Errors);

/// <summary>One dataset pinned to a document, for the Sources panel.</summary>
public sealed record SourceDto(
    string PackId,
    string PackTitle,
    string PackVersion,
    ReferencePackOrigin Origin,
    string? Locale,
    string DatasetId,
    ReferenceDatasetKind Kind,
    string? Title,
    int ItemCount,
    ReferenceAttribution Attribution,
    bool Used,
    string? ContentFingerprint);

public sealed record DocumentSourcesDto(IReadOnlyList<SourceDto> Datasets);

public sealed record ReferenceRecordDto(
    string Key,
    string Name,
    string? Category,
    long? Limit,
    string? Description,
    string? AssetId,
    string? Source,
    IReadOnlyDictionary<string, string>? Attributes,
    string PackId,
    string DatasetId);

public sealed record ReferencePageDto(
    string CatalogId,
    string PackId,
    IReadOnlyList<ReferenceRecordDto> Items,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<string> Categories)
{
    public IReadOnlyList<ReferenceSortKey> SortKeys { get; init; } = [];
}

/// <summary>A record with its full provenance.</summary>
public sealed record ReferenceRecordDetailDto(ReferenceRecordDto Record, string PackTitle, string PackVersion, ReferenceDatasetInfo Dataset);

internal static class ReferenceDtoMapper
{
    public static SourceDto ToDto(this PinnedDataset d) => new(
        d.Pack.Id,
        d.Pack.Title,
        d.Pack.Version,
        d.Pack.Origin,
        d.Pack.Locale,
        d.Dataset.Id,
        d.Dataset.Kind,
        d.Dataset.Title,
        d.Dataset.ItemCount,
        d.Dataset.Attribution,
        d.Used,
        d.Pack.ContentFingerprint);

    public static ReferenceRecordDto ToDto(this ReferenceRecord r) => new(
        r.Key,
        r.Name,
        r.Category,
        r.Limit,
        r.Description,
        r.AssetId,
        r.Source,
        r.Attributes.Count == 0 ? null : r.Attributes,
        r.Dataset.PackId,
        r.Dataset.Id);
}
