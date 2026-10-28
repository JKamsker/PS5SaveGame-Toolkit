using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>F8 adapter for the large observed-equipment identifier list.</summary>
internal sealed class TsushimaQueryableCollections(TsushimaReferenceViews views, bool hasIdentifiers)
    : IQueryableDocument
{
    public IReadOnlyList<QueryableCollection> Collections => hasIdentifiers ? [views.EquipmentCollection] : [];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Page size must be between 1 and 200.");
        }

        if (query.Offset < 0 || query.Cursor is not null)
        {
            throw new ArgumentException("Equipment queries use a non-negative offset without a cursor.", nameof(query));
        }

        return collectionId == TsushimaReferenceViews.EquipmentCollectionId
            ? views.QueryEquipment(query)
            : throw new KeyNotFoundException($"Unknown collection '{collectionId}'.");
    }
}
