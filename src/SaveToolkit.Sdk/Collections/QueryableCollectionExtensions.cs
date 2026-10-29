namespace SaveToolkit.Sdk.Collections;

/// <summary>View construction helpers that preserve queryable collection metadata.</summary>
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
