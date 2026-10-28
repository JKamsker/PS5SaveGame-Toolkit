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
        _ => node,
    };
}
