using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Api.Contracts;

public sealed record QueryableColumnDto(
    string Key,
    string Label,
    FieldSpec? Spec,
    string? Description,
    ColumnWidth Width);

public sealed record QueryablePageDto(
    string CollectionId,
    IReadOnlyList<QueryableColumnDto> Columns,
    IReadOnlyList<TableRow> Rows,
    long TotalCount,
    long FilteredCount,
    int Offset,
    int Limit,
    string? NextCursor,
    long Revision)
{
    public static QueryablePageDto From(QueryableCollectionPage page) => new(
        page.CollectionId,
        [.. page.Columns.Select(c => new QueryableColumnDto(c.Key, c.Label, c.Spec, c.Description, c.Width))],
        page.Rows,
        page.TotalCount,
        page.FilteredCount,
        page.Offset,
        page.Limit,
        page.NextCursor,
        page.Revision);
}
