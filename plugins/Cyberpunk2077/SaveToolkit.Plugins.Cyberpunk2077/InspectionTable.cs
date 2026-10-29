using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record InspectionRow(string Key, IReadOnlyDictionary<string, string> Values,
    string Attribution = ScriptableReader.Source, Confidence Confidence = Confidence.Inferred, bool IsEmpty = false);

/// <summary>Read-only tables carry provenance on every materialized row, including catalog-only rows.</summary>
internal sealed class InspectionTable
{
    private readonly InspectionCollection<InspectionRow> _collection;
    public InspectionTable(string id, string title, QueryableColumn[] columns, IReadOnlyList<InspectionRow> rows, string description,
        Func<InspectionRow, string, string>? liveValue = null, Func<InspectionRow, string, ViewNode?>? cell = null,
        EmptyRowPresentation? emptyRows = null)
    {
        string Value(InspectionRow row, string column) => liveValue?.Invoke(row, column) ?? row.Values.GetValueOrDefault(column, "");
        _collection = new(new QueryableCollection(id, title, columns, rows.Count) { Description = description, EmptyRows = emptyRows }, rows,
            row => row.Key, columns.ToDictionary(c => c.Key, c => (Func<InspectionRow, string>)(r => Value(r, c.Key))),
            row => new TableRow(row.Key, columns.ToDictionary(c => c.Key, c => cell?.Invoke(row, c.Key) ?? new SectionNode(
                Value(row, c.Key), [new TextNode(row.Attribution)])
                { Description = row.Attribution, Badge = row.Confidence.ToString() })) { IsEmpty = row.IsEmpty },
            isEmpty: row => row.IsEmpty);
    }
    public QueryableCollection Metadata => _collection.Metadata;
    public QueryableTableNode View() => _collection.View();
    public QueryableCollectionPage Query(QueryableCollectionQuery query) => _collection.Query(query);
}
