using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk;

internal sealed class DocumentMemberViewMapper(DocumentMemberAdapter member, DocumentMemberIdentityPolicy policy)
{
    public QueryableCollection Collection(QueryableCollection collection) => collection with
    {
        Id = member.Prefix(collection.Id),
        Title = policy.CollectionTitle(member.Role, collection.Title),
        Columns = collection.Columns.Select(column => policy.CollectionColumn(member.Document, column)).ToArray(),
    };

    public QueryableCollectionPage Page(QueryableCollectionPage page) => page with
    {
        CollectionId = member.Prefix(page.CollectionId),
        Rows = page.Rows.Select(row => row with
        {
            Key = member.Prefix(policy.QueryRowIdentity(page.CollectionId, row.Key)),
            Cells = row.Cells.ToDictionary(pair => pair.Key,
                pair => Node(pair.Value, page.CollectionId + "/" + row.Key + "/" + pair.Key), StringComparer.Ordinal),
        }).ToArray(),
    };

    public ViewNode Node(ViewNode node, string scope) => node switch
    {
        FieldNode field => field with { ValueId = member.Prefix(field.ValueId) },
        SectionNode section => section with
        {
            LazyId = section.LazyId is { } id ? member.Prefix(id) : null,
            Children = section.Children.Select((child, index) => Node(child, scope + "/" + index)).ToArray(),
        },
        TabsNode tabs => tabs with
        {
            Tabs = tabs.Tabs.Select((tab, index) => tab with
            {
                Children = tab.Children.Select((child, childIndex) => Node(child, scope + "/" + index + "/" + childIndex)).ToArray(),
            }).ToArray(),
        },
        TableNode table => table with
        {
            SelectionId = table.SelectionId is { } selectionId ? member.Prefix(selectionId) : null,
            Rows = table.Rows.Select(row => row with
            {
                Key = member.Prefix(policy.TableRowIdentity(scope, row.Key)),
                Cells = row.Cells.ToDictionary(pair => pair.Key,
                    pair => Node(pair.Value, policy.TableCellScope(scope, row.Key, pair.Key)), StringComparer.Ordinal),
            }).ToArray(),
        },
        QueryableTableNode table => table with { CollectionId = member.Prefix(table.CollectionId) },
        // Parameter ValueIds are dictionary keys in the local action contract, not document fields.
        ActionNode action => action with
        {
            ActionId = member.Prefix(action.ActionId),
            SelectionTableId = action.SelectionTableId is { } selectionId ? member.Prefix(selectionId) : null,
        },
        HexNode hex => hex with { BlobId = member.Prefix(hex.BlobId) },
        GraphNode graph => graph with
        {
            Items = graph.Items.Select(item => item with
            {
                Id = policy.PrefixGraphIds ? member.Prefix(item.Id) : item.Id, Target = Target(item.Target),
            }).ToArray(),
            Edges = graph.Edges.Select(edge => policy.PrefixGraphIds
                ? edge with { From = member.Prefix(edge.From), To = member.Prefix(edge.To) } : edge).ToArray(),
        },
        MapNode map => map with
        {
            Markers = map.Markers.Select(marker => marker with
            {
                Id = policy.PrefixMapMarkerIds ? member.Prefix(marker.Id) : marker.Id, Target = Target(marker.Target),
            }).ToArray(),
        },
        _ => node,
    };

    private ViewTarget? Target(ViewTarget? target) => target is null ? null : target with
    {
        Id = member.Prefix(target.Id), Path = policy.NavigationPath(member.Role, target.Path),
    };
}
