using SaveToolkit.Sdk.Views;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal static class FaithViewFields
{
    public static IEnumerable<FieldNode> All(DocumentSession session)
    {
        foreach (var field in All(session.BuildView())) yield return field;
        foreach (var collection in session.QueryableCollections)
            for (var offset = 0; offset < collection.TotalCount; offset += 200)
                foreach (var row in session.QueryCollection(collection.Id, new QueryableCollectionQuery { Offset = offset, Limit = 200 }).Rows)
                    foreach (var field in row.Cells.Values.SelectMany(All)) yield return field;
    }

    public static IEnumerable<FieldNode> All(ViewNode root) => FaithViewNodes.All(root).OfType<FieldNode>();
}
