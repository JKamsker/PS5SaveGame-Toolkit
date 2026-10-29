using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal interface ICollectionProjection
{
    QueryableCollection Metadata { get; }
    QueryableTableNode View();
    QueryableCollectionPage Query(QueryableCollectionQuery query);
}
