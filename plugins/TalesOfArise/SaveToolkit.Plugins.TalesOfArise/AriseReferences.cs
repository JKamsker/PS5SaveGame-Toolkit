using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Read-only catalog enrichment; a reference identity never authorizes a save mutation.</summary>
internal sealed class AriseReferences(IReferenceContext context)
{
    public const string ItemsCatalogId = "items";
    public const string EquipmentCatalogId = "equipment";
    public const string ArtesCatalogId = "artes";
    public const string SkillsCatalogId = "skills";
    public const string TitlesCatalogId = "titles";
    public const string SkillNodesCatalogId = "skill-nodes";
    public const string CharactersCatalogId = "characters";
    public const string ProgressCatalogId = "progress";
    private const string InspectPrefix = "references/inspect/";
    private static readonly string[] CatalogIds =
        [ItemsCatalogId, EquipmentCatalogId, ArtesCatalogId, SkillsCatalogId, TitlesCatalogId, SkillNodesCatalogId, CharactersCatalogId, ProgressCatalogId];
    private readonly IReadOnlyDictionary<string, IReferenceCatalog?> _catalogs =
        CatalogIds.ToDictionary(id => id, id => context.Catalog(id), StringComparer.Ordinal);

    public IReferenceCatalog? Items => _catalogs[ItemsCatalogId];

    public ReferenceRecord? Character(int index) => _catalogs[CharactersCatalogId]?
        .Search(new ReferenceQuery { Category = "Playable", Limit = ReferenceQuery.MaxLimit }).Items
        .SingleOrDefault(record => record.Attributes.GetValueOrDefault("statusIndex") == index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public IReadOnlyList<ViewNode> BuildView() =>
    [
        new TextNode("Pinned Aselia Wiki reference data, CC BY-SA 3.0. Catalog keys do not establish saved ownership, recruitment, learned skills, completion or safe replacement. Published DLC entries do not establish DLC save support.") { Tone = Tone.Info },
        CatalogSection(ItemsCatalogId),
        CatalogSection(EquipmentCatalogId),
        CatalogSection(CharactersCatalogId),
    ];

    public bool Handles(string actionId) => actionId.StartsWith(InspectPrefix, StringComparison.Ordinal);

    public ActionOutcome Inspect(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var id = actionId[InspectPrefix.Length..];
        var catalog = CatalogOrThrow(id);
        if (!parameters.TryGetValue("record", out var value) || value.Kind != FieldValueKind.Text ||
            catalog.Find(value.AsString()) is not { } record)
        {
            throw new ArgumentException("Choose an existing catalog record.", nameof(parameters));
        }

        return new ActionOutcome
        {
            Message = $"{record.Name} · ID {record.Key} · {record.Category ?? "Uncategorised"}\n" +
                $"{record.Description}\n{record.AttributionText}\nInspection only; save state is unknown and no bytes were changed.",
        };
    }

    public IReadOnlyList<QueryableCollection> Collections => CatalogIds.Select(id =>
        new QueryableCollection(id, _catalogs[id]?.Dataset.Title ?? id, AriseCatalogQuery.Columns(id), _catalogs[id]?.Count ?? 0)
        {
            Description = _catalogs[id]?.Dataset.Attribution.ToShortText() ?? "No matching reference pack is pinned.",
        }).ToArray();

    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query)
    {
        if (!_catalogs.TryGetValue(id, out var catalog))
        {
            throw new KeyNotFoundException($"Unknown Tales of Arise catalog '{id}'.");
        }

        return catalog is null
            ? new QueryableCollectionPage(id, AriseCatalogQuery.Columns(id), [], 0, 0,
                Math.Max(0, query.Offset), Math.Clamp(query.Limit, 1, ReferenceQuery.MaxLimit))
            : AriseCatalogQuery.Query(id, catalog, query);
    }

    public ViewNode CatalogSection(string id, string? character = null)
    {
        var catalog = _catalogs[id];
        return catalog is null
            ? new SectionNode(id, [new TextNode("No matching reference pack is pinned. Raw save identities remain available.") { Tone = Tone.Muted }])
            : new SectionNode(catalog.Dataset.Title ?? id,
            [
                new TextNode($"{catalog.Count} records · {catalog.Pack.Id} {catalog.Pack.Version} · {catalog.Dataset.Attribution.ToShortText()}"),
                new QueryableTableNode(id, catalog.Dataset.Title ?? id, AriseCatalogQuery.Columns(id), catalog.Count)
                {
                    Description = "Search names, IDs, prerequisites and facts; exact column filters and deterministic sorting cover every catalog page." +
                        (character is null ? "" : $" Use the Character filter: {character}."),
                },
                new ActionNode(InspectPrefix + id, "Inspect a reference record")
                {
                    Description = catalog.Dataset.Attribution.ToShortText(),
                    Parameters = [new FieldNode("record", "Reference record", new ReferenceSpec(id) { ReadOnly = true },
                        FieldValue.Of(catalog.Search(new ReferenceQuery { Limit = 1 }).Items.FirstOrDefault()?.Key ?? ""))],
                },
            ]);
    }

    internal IReferenceCatalog? Catalog(string id) => _catalogs.GetValueOrDefault(id);

    private IReferenceCatalog CatalogOrThrow(string id) => Catalog(id)
        ?? throw new KeyNotFoundException($"Tales of Arise catalog '{id}' is unavailable.");
}
