using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.References;

/// <summary>
/// A read-only, bounded catalog of records (one records dataset of one pack). Keyed lookup is O(1);
/// browsing goes through <see cref="Search"/>, which always pages.
/// </summary>
public interface IReferenceCatalog
{
    /// <summary>Catalog id, equal to the dataset id in the pack manifest (e.g. "items").</summary>
    string Id { get; }

    ReferencePackInfo Pack { get; }

    ReferenceDatasetInfo Dataset { get; }

    int Count { get; }

    /// <summary>Distinct categories in file order.</summary>
    IReadOnlyList<string> Categories { get; }

    /// <summary>Supported sort keys. Empty preserves compatibility with custom search-only catalogs.</summary>
    IReadOnlyList<ReferenceSortKey> SortKeys => [];

    /// <summary>Record with exactly this key (ordinal comparison), or null.</summary>
    ReferenceRecord? Find(string key);

    /// <summary>Case-insensitive match on key/name and category, optional declared sorting, always paged.</summary>
    ReferencePage Search(ReferenceQuery query);
}

public sealed record ReferenceQuery
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>Substring to look for in key or name; null or empty matches everything.</summary>
    public string? Text { get; init; }

    /// <summary>Exact category (case-insensitive); null for all.</summary>
    public string? Category { get; init; }

    /// <summary>Optional declared sort key; null retains catalog order. Ties use the ordinal raw key.</summary>
    public ReferenceSortKey? SortBy { get; init; }

    public bool Descending { get; init; }

    public int Offset { get; init; }

    /// <summary>Page size, clamped to 1..<see cref="MaxLimit"/>.</summary>
    public int Limit { get; init; } = DefaultLimit;
}

[JsonConverter(typeof(JsonStringEnumConverter<ReferenceSortKey>))]
public enum ReferenceSortKey
{
    Key,
    Name,
    Category,
}

/// <param name="Items">The page, in the requested sort order, or catalog order when omitted.</param>
/// <param name="Total">Matches across all pages.</param>
/// <param name="Offset">Offset of the first item.</param>
public sealed record ReferencePage(IReadOnlyList<ReferenceRecord> Items, int Total, int Offset);
