using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk.Collections;

/// <summary>Optional document capability for collections that are too large to emit eagerly.</summary>
public interface IQueryableDocument
{
    /// <summary>Collection metadata with stable IDs, column keys and complete unfiltered counts.</summary>
    IReadOnlyList<QueryableCollection> Collections { get; }

    /// <summary>
    /// Filters and sorts the complete model, then returns a bounded page with stable row keys.
    /// Rendered editable fields must register the same bindings as ordinary document views.
    /// Resolve an omitted IncludeEmpty from the collection's presentation policy for direct callers.
    /// </summary>
    QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query);
}

/// <summary>Stable metadata for one server-backed collection.</summary>
public sealed record QueryableCollection(
    string Id,
    string Title,
    IReadOnlyList<QueryableColumn> Columns,
    long TotalCount)
{
    /// <summary>Optional policy for confirmed empty slots, applied before sorting and paging.</summary>
    public EmptyRowPresentation? EmptyRows { get; init; }

    /// <summary>Optional collection explanation or attribution.</summary>
    public string? Description { get; init; }
}

/// <summary>A typed column in a queryable collection. Cells still carry their complete view node.</summary>
public sealed record QueryableColumn(string Key, string Label, FieldSpec? Spec = null)
{
    /// <summary>Optional column explanation.</summary>
    public string? Description { get; init; }

    /// <summary>Relative column width hint.</summary>
    public ColumnWidth Width { get; init; } = ColumnWidth.Normal;
}

/// <summary>Text, exact column filters and deterministic paging/sorting for a collection.</summary>
public sealed record QueryableCollectionQuery
{
    /// <summary>Null uses collection EmptyRows.HideByDefault; legacy collections include all rows.
    /// Filter emptiness on the complete model before paging. Core supplies the resolved value.</summary>
    public bool? IncludeEmpty { get; init; }

    /// <summary>Optional free-text search over the collection's searchable model projections.</summary>
    public string? Text { get; init; }

    /// <summary>Exact filters keyed by declared column keys; apply them before visibility filtering and paging.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Declared column key to sort by, or null for deterministic native ordering.</summary>
    public string? SortColumn { get; init; }

    /// <summary>Requests descending sorting; use a stable identity tie-break in either direction.</summary>
    public bool Descending { get; init; }

    /// <summary>Zero-based position among matching visible rows; must be zero when using a cursor.</summary>
    public int Offset { get; init; }

    /// <summary>Maximum rows to return, between one and <see cref="QueryableCollectionLimits.MaxPageSize"/>.</summary>
    public int Limit { get; init; } = 50;

    /// <summary>Opaque continuation token. A request uses either this or <see cref="Offset"/>.</summary>
    public string? Cursor { get; init; }
}

/// <summary>One bounded result page. Rows use stable keys and ordinary view cells.</summary>
/// <param name="CollectionId">The queried collection's stable ID.</param>
/// <param name="Columns">The collection's declared columns.</param>
/// <param name="Rows">Requested page of matching visible rows; never more than Limit.</param>
/// <param name="TotalCount">Every model row, including confirmed empty slots and nonmatching rows.</param>
/// <param name="FilteredCount">Matching visible rows across all pages, after empty-slot filtering.</param>
/// <param name="Offset">Zero-based position of this page among matching visible rows.</param>
/// <param name="Limit">The requested page-size limit, including on a short final page.</param>
public sealed record QueryableCollectionPage(
    string CollectionId,
    IReadOnlyList<QueryableColumn> Columns,
    IReadOnlyList<TableRow> Rows,
    long TotalCount,
    long FilteredCount,
    int Offset,
    int Limit)
{
    /// <summary>Rows matching text/column filters excluded only by IncludeEmpty=false, before paging. Zero when shown.</summary>
    public long HiddenEmptyCount { get; init; }

    /// <summary>Opaque token for the next page, or null when no cursor continuation is supplied.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Assigned by Core when the page is served; plugins do not need to track revisions.</summary>
    public long Revision { get; init; }
}

/// <summary>Metadata emitted in the document view to select the server-backed table renderer.</summary>
public sealed record QueryableTableNode(
    string CollectionId,
    string Title,
    IReadOnlyList<QueryableColumn> Columns,
    long TotalCount) : ViewNode
{
    /// <summary>Use the same settings as the collection metadata.</summary>
    public EmptyRowPresentation? EmptyRows { get; init; }

    /// <summary>Optional collection explanation or attribution, matching the collection metadata.</summary>
    public string? Description { get; init; }
}
