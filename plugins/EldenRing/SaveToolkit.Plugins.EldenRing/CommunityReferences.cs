using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class CommunityReferences
{
    public const string PackId = "eldenring.community.en";
    public const string ItemsCatalogId = "eldenring-tga-items";
    public const string FlagsCatalogId = "eldenring-tga-flags";
    public const string ParamLayoutsCatalogId = "eldenring-param-layouts";
    public const string FlagSchema = "Schema: eldenring-community-flags/2.";
    public const string ProgressSectionId = "community-progress";
    private readonly IReadOnlyList<CommunityCatalogView> _catalogs;
    private readonly bool _hasCharacters;

    public CommunityReferences(IReferenceContext references, string fingerprint, byte[] bytes,
        IReadOnlyList<SemanticCharacter> characters)
    {
        _hasCharacters = characters.Count > 0;
        Items = references.Catalog(ItemsCatalogId, fingerprint);
        Layouts = references.Catalog(ParamLayoutsCatalogId, fingerprint);
        Progress = new(bytes, characters, references.Catalog(FlagsCatalogId, fingerprint), FlagsCatalogId, ProgressSectionId,
            ProgressSectionId, references);
        _catalogs =
        [
            new("catalog/community-items", "Community item names and groups", Items),
            new("catalog/param-layouts", "Technical PARAM layout facts", Layouts),
            new("catalog/community-flags", "All community flag references (including unmapped candidates)",
                references.Catalog(FlagsCatalogId, fingerprint)),
        ];
    }

    public IReferenceCatalog? Items { get; }
    public IReferenceCatalog? Layouts { get; }
    public ProgressViews Progress { get; }
    public IReadOnlyList<ViewNode> ProgressSections => _hasCharacters
        ? [SectionNode.Lazy("Community base/DLC/NG+ source candidates (spoilers)", ProgressSectionId)] : [];
    public IReadOnlyList<QueryableCollection> Collections => [.. _catalogs.Select(view => view.Collection), .. Progress.Collections];
    public IReadOnlyList<ViewNode> CatalogSections => _catalogs.Select(view =>
        (ViewNode)SectionNode.Lazy(view.Collection.Title, view.Collection.Id)).ToArray();
    public bool OwnsCatalog(string id) => _catalogs.Any(view => view.Collection.Id == id);
    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query) =>
        _catalogs.Single(view => view.Collection.Id == id).Query(query);
    public IReadOnlyList<ViewNode> ExpandCatalog(string id) => _catalogs.Single(view => view.Collection.Id == id).View();
}

internal sealed class CommunityCatalogView(string id, string title, IReferenceCatalog? catalog)
{
    private const int MaximumRecords = 20_000;
    private readonly Lazy<IReadOnlyList<ReferenceRecord>> _index = new(() =>
    {
        if (catalog is null || catalog.Count == 0) return [];
        if (catalog.Count is < 0 or > MaximumRecords)
            throw new InvalidOperationException($"Community catalog exceeds the {MaximumRecords}-record budget.");
        var records = ReferenceCatalogReader.ReadBounded(catalog, new() { Limit = ReferenceQuery.MaxLimit }, MaximumRecords);
        // This unfiltered index must cover the whole catalog, not just a consistent query subset.
        if (records.Count != catalog.Count)
            throw new InvalidOperationException("Community catalog returned incomplete or duplicate pages.");
        return records;
    });
    private readonly ReferenceSearchText _searchText = new();
    private static IReadOnlyList<QueryableColumn> Columns =>
        [new("id", "Source identity"), new("name", "Name / declaration"), new("category", "Category / table"),
            new("groups", "Source groups"), new("details", "Evidence / attribution")];

    public QueryableCollection Collection => new(id, title, Columns, catalog?.Count ?? 0)
    {
        Description = "Read-only community facts; identities and layout compatibility are Inferred on PS5. "
            + (catalog?.Dataset.Attribution.ToShortText() ?? "No compatible reference pack."),
    };

    public IReadOnlyList<ViewNode> View() => [new QueryableTableNode(id, title, Columns, Collection.TotalCount)
        { Description = Collection.Description }];

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        var records = ReferenceSearchText.Filter(_index.Value, query, Matches);
        var result = BrowseQuery.Apply(records, query with { Text = null }, Columns, row => row.Key, Text, (_, _) => null);
        return new(id, Columns, result.Page.Select(record => new TableRow(id + "/" + record.Key,
            Columns.ToDictionary(column => column.Key, column => (ViewNode)new TextNode(Text(record, column.Key))))).ToArray(),
            _index.Value.Count, result.Filtered, result.Offset, result.Limit);
    }

    private static string Text(ReferenceRecord record, string column) => column switch
    {
        "id" => record.Key, "name" => record.Name,
        "category" => record.Category ?? (record.Dataset.Id == CommunityReferences.FlagsCatalogId ? "event-flag" : "Unknown"),
        "groups" => string.Join(" · ", record.Attributes.Where(attribute =>
            attribute.Key is "groups" or "editorGroups" or "contentGroups" or "contentGroup" or "sourceCategory" or "flagRole").Select(attribute => attribute.Value)),
        "details" => ReferenceBrowser.Details(record) + " · "
            + string.Join(" · ", record.Attributes.Select(attribute => $"{attribute.Key}: {attribute.Value}")),
        _ => throw new ArgumentException("Unknown community catalog column."),
    };

    private bool Matches(ReferenceRecord record, string search)
    {
        // Groups and evidence both join parts with middle dots. Preserve substrings across
        // those boundaries, including the boundary between provenance and attributes.
        if (search.Contains('·')) return Columns.Any(column => ReferenceSearchText.Contains(Text(record, column.Key), search));
        return ReferenceSearchText.Contains(record.Key, search) || ReferenceSearchText.Contains(record.Name, search)
            || ReferenceSearchText.Contains(Text(record, "category"), search) || _searchText.ContainsDetails(record, search)
            || record.Attributes.Any(attribute => ReferenceSearchText.Contains(attribute.Key, search)
                || ReferenceSearchText.Contains(attribute.Value, search)
                || search.Contains(':') && ReferenceSearchText.Contains(attribute.Key + ": " + attribute.Value, search));
    }
}
