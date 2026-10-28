using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Namespace member identities while retaining the underlying writable contracts.</summary>
internal sealed class GearboxGroupMember(string role, GearboxDocument document)
{
    public string Role { get; } = role;
    public GearboxDocument Document { get; } = document;
    public string Prefix(string id) => Role + "/" + id;
    public string Local(string id) => id.StartsWith(Role + "/", StringComparison.Ordinal)
        ? id[(Role.Length + 1)..] : throw new KeyNotFoundException("Unknown Gearbox group identity.");

    public QueryableCollection Collection(QueryableCollection collection) => collection with
    { Id = Prefix(collection.Id), Title = Role + ": " + collection.Title };

    public QueryableCollectionPage Page(QueryableCollectionPage page) => page with
    { CollectionId = Prefix(page.CollectionId), Rows = page.Rows.Select(r => Row(r, page.CollectionId)).ToArray() };

    private TableRow Row(TableRow row, string scope) => row with
    { Key = Prefix(scope + "/" + row.Key), Cells = row.Cells.ToDictionary(p => p.Key, p => Node(p.Value, scope + "/" + row.Key + "/" + p.Key), StringComparer.Ordinal) };

    public ViewNode Node(ViewNode node, string scope = "view") => node switch
    {
        FieldNode field => field with { ValueId = Prefix(field.ValueId) },
        SectionNode section => section with { LazyId = section.LazyId is { } id ? Prefix(id) : null,
            Children = section.Children.Select((n, i) => Node(n, scope + "/" + i)).ToArray() },
        TabsNode tabs => tabs with { Tabs = tabs.Tabs.Select((t, i) => t with {
            Children = t.Children.Select((n, j) => Node(n, scope + "/" + i + "/" + j)).ToArray() }).ToArray() },
        TableNode table => table with { Rows = table.Rows.Select(r => Row(r, scope)).ToArray() },
        QueryableTableNode table => table with { CollectionId = Prefix(table.CollectionId) },
        ActionNode action => action with { ActionId = Prefix(action.ActionId) },
        HexNode hex => hex with { BlobId = Prefix(hex.BlobId) },
        GraphNode graph => graph with { Items = graph.Items.Select(item => item with { Id = Prefix(item.Id), Target = Target(item.Target) }).ToArray(),
            Edges = graph.Edges.Select(edge => new GraphEdge(Prefix(edge.From), Prefix(edge.To))).ToArray() },
        MapNode map => map with { Markers = map.Markers.Select(marker => marker with { Id = Prefix(marker.Id), Target = Target(marker.Target) }).ToArray() },
        _ => node,
    };

    private ViewTarget? Target(ViewTarget? target) => target is null ? null : target with
    {
        Id = Prefix(target.Id),
        Path = ["section:Gearbox character + profile", Role == GearboxGroups.ProfileRole ? "section:Shared profile / bank" : "section:Character progression / backpack",
            .. target.Path.Select(part => part.StartsWith("section:", StringComparison.Ordinal) ? "section:" + Prefix(part[8..]) : part)],
    };
}
