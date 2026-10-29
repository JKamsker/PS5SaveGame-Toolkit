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

    /// <summary>The pinned pack supplying this catalog.</summary>
    ReferencePackInfo Pack { get; }

    /// <summary>Record-dataset identity and provenance for this catalog.</summary>
    ReferenceDatasetInfo Dataset { get; }

    /// <summary>Total records before query filtering.</summary>
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

/// <summary>Bounded catalog search combining substring text, exact category and an optional declared sort order.</summary>
public sealed record ReferenceQuery
{
    /// <summary>Default requested records per page.</summary>
    public const int DefaultLimit = 50;
    /// <summary>Maximum requested records per page.</summary>
    public const int MaxLimit = 200;

    /// <summary>Substring to look for in key or name; null or empty matches everything.</summary>
    public string? Text { get; init; }

    /// <summary>Exact category (case-insensitive); null for all.</summary>
    public string? Category { get; init; }

    /// <summary>Optional declared sort key; null retains catalog order. Ties use the ordinal raw key.</summary>
    public ReferenceSortKey? SortBy { get; init; }

    /// <summary>Reverses the declared sort key; equal values retain an ascending ordinal raw-key tie-break.</summary>
    public bool Descending { get; init; }

    /// <summary>Zero-based starting position among matching records, clamped to zero by host catalogs.</summary>
    public int Offset { get; init; }

    /// <summary>Page size, clamped to 1..<see cref="MaxLimit"/>.</summary>
    public int Limit { get; init; } = DefaultLimit;
}

/// <summary>Catalog sort keys; callers may request only keys declared by the catalog.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferenceSortKey>))]
public enum ReferenceSortKey
{
    /// <summary>Raw record key.</summary>
    Key,
    /// <summary>Display name.</summary>
    Name,
    /// <summary>Category, with raw-key ties.</summary>
    Category,
}

/// <summary>A bounded catalog page with the full matching count and the effective starting offset.</summary>
/// <param name="Items">The page, in the requested sort order, or catalog order when omitted.</param>
/// <param name="Total">Matches across all pages.</param>
/// <param name="Offset">Offset of the first item.</param>
public sealed record ReferencePage(IReadOnlyList<ReferenceRecord> Items, int Total, int Offset);
