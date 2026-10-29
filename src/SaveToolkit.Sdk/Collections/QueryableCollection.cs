using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk.Collections;

/// <summary>Optional document capability for collections that are too large to emit eagerly.</summary>
public interface IQueryableDocument
{
    IReadOnlyList<QueryableCollection> Collections { get; }

    QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query);
}

/// <summary>Stable metadata for one server-backed collection.</summary>
public sealed record QueryableCollection(
    string Id,
    string Title,
    IReadOnlyList<QueryableColumn> Columns,
    long TotalCount)
{
    public EmptyRowPresentation? EmptyRows { get; init; }

    public string? Description { get; init; }
}

/// <summary>A typed column in a queryable collection. Cells still carry their complete view node.</summary>
public sealed record QueryableColumn(string Key, string Label, FieldSpec? Spec = null)
{
    public string? Description { get; init; }

    public ColumnWidth Width { get; init; } = ColumnWidth.Normal;
}

/// <summary>Text, exact column filters and deterministic paging/sorting for a collection.</summary>
public sealed record QueryableCollectionQuery
{
    /// <summary>Null uses collection EmptyRows.HideByDefault; legacy collections include all rows.
    /// Filter emptiness on the complete model before paging. Core supplies the resolved value.</summary>
    public bool? IncludeEmpty { get; init; }

    public string? Text { get; init; }

    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public string? SortColumn { get; init; }

    public bool Descending { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = 50;

    /// <summary>Opaque continuation token. A request uses either this or <see cref="Offset"/>.</summary>
    public string? Cursor { get; init; }
}

/// <summary>One bounded result page. Rows use stable keys and ordinary view cells.</summary>
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

    public string? Description { get; init; }
}
