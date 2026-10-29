namespace SaveToolkit.Sdk.Collections;

public static class QueryableCollectionExtensions
{
    /// <summary>Preserves the collection's descriptions and empty-row presentation in its view shell.</summary>
    public static QueryableTableNode ToTableNode(this QueryableCollection metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new(metadata.Id, metadata.Title, metadata.Columns, metadata.TotalCount)
        { Description = metadata.Description, EmptyRows = metadata.EmptyRows };
    }
}
