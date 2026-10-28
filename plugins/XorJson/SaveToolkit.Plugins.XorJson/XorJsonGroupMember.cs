using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class XorJsonGroupMember(string role, JsonSaveDocument document)
{
    public string Role { get; } = role;
    public JsonSaveDocument Document { get; } = document;

    public string Prefix(string id) => Role + "/" + id;

    public string Local(string id) => id.StartsWith(Role + "/", StringComparison.Ordinal)
        ? id[(Role.Length + 1)..]
        : throw new KeyNotFoundException("Unknown XorJson group member identity.");

    public QueryableCollection Collection(QueryableCollection collection) => collection with
    {
        Id = Prefix(collection.Id),
        Title = Role + ": " + collection.Title,
        Columns = collection.Columns.Select(c => c with
        {
            Spec = c.Spec is { } spec ? spec with { ReadOnly = !Document.CanEdit } : null,
        }).ToArray(),
    };

    public QueryableCollectionPage Page(QueryableCollectionPage page) => page with
    {
        CollectionId = Prefix(page.CollectionId),
        Rows = page.Rows.Select(row => row with
        {
            Key = Prefix(row.Key),
            Cells = row.Cells.ToDictionary(pair => pair.Key,
                pair => Node(pair.Value, page.CollectionId + "/" + row.Key + "/" + pair.Key), StringComparer.Ordinal),
        }).ToArray(),
    };

    public ViewNode Node(ViewNode node, string scope = "view") => node switch
    {
        FieldNode field => field with { ValueId = Prefix(field.ValueId) },
        SectionNode section => section with
        {
            LazyId = section.LazyId is { } lazy ? Prefix(lazy) : null,
            Children = section.Children.Select((child, index) => Node(child, scope + "/" + index)).ToArray(),
        },
        TabsNode tabs => tabs with
        {
            Tabs = tabs.Tabs.Select((tab, index) => tab with
            {
                Children = tab.Children.Select((child, childIndex) => Node(child, scope + "/" + index + "/" + childIndex)).ToArray(),
            }).ToArray(),
        },
        QueryableTableNode table => table with { CollectionId = Prefix(table.CollectionId) },
        TableNode table => table with
        {
            Rows = table.Rows.Select(row => row with
            {
                Key = Prefix(scope + "/" + row.Key),
                Cells = row.Cells.ToDictionary(pair => pair.Key, pair => Node(pair.Value, scope + "/" + row.Key), StringComparer.Ordinal),
            }).ToArray(),
        },
        ActionNode action => action with { ActionId = Prefix(action.ActionId) },
        GraphNode graph => graph with { Items = graph.Items.Select(item => item with { Target = Target(item.Target) }).ToArray() },
        MapNode map => map with { Markers = map.Markers.Select(marker => marker with { Target = Target(marker.Target) }).ToArray() },
        HexNode hex => hex with { BlobId = Prefix(hex.BlobId) },
        _ => node,
    };

    public void Dispose() => Document.Dispose();

    private ViewTarget? Target(ViewTarget? target) => target is null ? null : target with
    {
        Id = Prefix(target.Id),
        Path = ["section:XorJson character + global", "section:" + Role, .. target.Path],
    };
}
