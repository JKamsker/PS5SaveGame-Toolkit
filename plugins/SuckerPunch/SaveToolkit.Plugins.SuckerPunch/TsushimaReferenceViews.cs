using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Read-only, attributed interpretation of identifiers and unjoined wiki reference data.</summary>
internal sealed class TsushimaReferenceViews(
    IReferenceContext references,
    IReadOnlyList<TsushimaIdentifier> identifiers,
    Func<FieldNode, FieldNode> bind)
{
    private const int GuidePageSize = 200;
    public const string EquipmentCollectionId = "equipment/identifiers";

    public static IReadOnlyList<ViewNode> EquipmentGuides =>
    [
        SectionNode.Lazy("Charm reference — save IDs unmapped", TsushimaIds.GuidePrefix + TsushimaIds.CharmGuide),
        SectionNode.Lazy("Armor reference — save IDs unmapped", TsushimaIds.GuidePrefix + TsushimaIds.ArmorGuide),
        SectionNode.Lazy("Sword kit reference", TsushimaIds.GuidePrefix + TsushimaIds.SwordGuide),
    ];

    public static SectionNode ResourceGuide => SectionNode.Lazy("Resource reference", TsushimaIds.GuidePrefix + TsushimaIds.ResourceGuide);
    public static SectionNode TechniqueGuide => SectionNode.Lazy("Technique reference", TsushimaIds.GuidePrefix + TsushimaIds.TechniqueGuide);
    public static SectionNode WorldGuide => SectionNode.Lazy("Shrines by region", TsushimaIds.GuidePrefix + TsushimaIds.WorldGuide);

    public TableNode Inventory(TsushimaInventory inventory, Func<InventoryRecord, FieldNode> quantity,
        Func<InventoryRecord, FieldNode> mirror)
    {
        var catalog = references.Catalog(TsushimaIds.Items);
        var rows = inventory.Records.Select(record => new TableRow(record.Name, new Dictionary<string, ViewNode>
        {
            ["raw"] = new TextNode(record.Name) { Monospace = true },
            ["item"] = ReferenceField($"inventory/{record.Name}/reference", record.Name, TsushimaIds.Items, catalog),
            ["category"] = new TextNode(catalog?.Find(record.Name)?.Category ?? "Unknown"),
            ["quantity"] = quantity(record),
            ["copy"] = record.IsAmmo ? mirror(record) : new TextNode("—"),
            ["mapping"] = new TextNode(MappingConfidence(catalog?.Find(record.Name)).ToString()),
        })).ToArray();
        return new TableNode([new("raw", "Raw ID"), new("item", "Item"), new("category", "Category"),
            new("quantity", "Quantity"), new("copy", "Ammo count copy"), new("mapping", "Name mapping")], rows)
        {
            Title = "Existing inventory",
            Description = "48 length-prefixed records. Name confidence is separate from quantity confidence and write safety. " +
                "Capacity and mode-specific eligibility remain unproven; 32767 is an editor bound, not a carrying capacity.",
        };
    }

    public ViewNode Identifiers(IdentifierGroup group, string catalogId)
    {
        if (group == IdentifierGroup.Equipment)
        {
            return new QueryableTableNode(
                EquipmentCollectionId,
                "Observed equipment identifiers",
                IdentifierColumns(),
                identifiers.Count(identifier => identifier.Group == group))
            {
                Description = "Large identifier list is server-filtered and paged. Presence does not prove ownership, equipped state or write safety.",
            };
        }

        var catalog = references.Catalog(catalogId);
        var rows = identifiers.Where(identifier => identifier.Group == group).Select(identifier =>
            new TableRow(identifier.Name, new Dictionary<string, ViewNode>
            {
                ["raw"] = new TextNode(identifier.Name) { Monospace = true },
                ["reference"] = ReferenceField($"{catalogId}/{identifier.Name}/reference", identifier.Name, catalogId, catalog),
                ["location"] = new TextNode($"0x{identifier.Offset:X}") { Monospace = true },
                ["mapping"] = new TextNode(MappingConfidence(catalog?.Find(identifier.Name)).ToString()),
                ["state"] = new TextNode("Unknown") { Tone = Tone.Warning },
            })).ToArray();
        return new TableNode([new("raw", "Raw ID"), new("reference", "Reference"), new("location", "First offset"),
            new("mapping", "Name mapping"), new("state", "Ownership / state")], rows)
        {
            Title = group == IdentifierGroup.Equipment ? "Observed equipment identifiers" : "Observed technique identifiers",
            Description = catalog is null ? "No pinned reference pack; raw IDs remain visible." :
                $"Attributed dataset: {catalog.Dataset.Attribution.ToShortText()}",
        };
    }

    public QueryableCollection EquipmentCollection => new(
        EquipmentCollectionId,
        "Observed equipment identifiers",
        IdentifierColumns(),
        identifiers.Count(identifier => identifier.Group == IdentifierGroup.Equipment))
    {
        Description = "Observed identifiers are paged and searchable. Ownership, equipped state and write safety remain Unknown.",
    };

    public QueryableCollectionPage QueryEquipment(QueryableCollectionQuery query)
    {
        var catalog = references.Catalog(TsushimaIds.Equipment);
        IEnumerable<(TsushimaIdentifier Identifier, ReferenceRecord? Record)> filtered = identifiers
            .Where(identifier => identifier.Group == IdentifierGroup.Equipment)
            .Select(identifier => (Identifier: identifier, Record: catalog?.Find(identifier.Name)))
            .Where(item => Matches(item.Identifier, item.Record, query))
            .OrderBy(item => SortValue(item.Identifier, item.Record, query.SortColumn), StringComparer.Ordinal)
            .ThenBy(item => item.Identifier.Offset);
        if (query.Descending)
        {
            filtered = filtered.Reverse();
        }

        var total = identifiers.Count(identifier => identifier.Group == IdentifierGroup.Equipment);
        var matches = filtered.ToArray();
        var rows = matches
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(item => IdentifierRow(item.Identifier, item.Record, catalog))
            .ToArray();
        return new QueryableCollectionPage(
            EquipmentCollectionId,
            IdentifierColumns(),
            rows,
            total,
            matches.Length,
            query.Offset,
            query.Limit);
    }

    public TableNode Character()
    {
        var rows = identifiers.Where(identifier => identifier.Group == IdentifierGroup.Character).Select(identifier =>
            new TableRow(identifier.Name, new Dictionary<string, ViewNode>
            {
                ["identifier"] = new TextNode(identifier.Name) { Monospace = true },
                ["location"] = new TextNode($"0x{identifier.Offset:X}") { Monospace = true },
                ["relationship"] = new TextNode("Unknown") { Tone = Tone.Warning },
            })).ToArray();
        return new TableNode([new("identifier", "Observed identifier"), new("location", "First offset"),
            new("relationship", "Relationship")], rows)
        { Title = "Character and progression candidates", Description = "Identifier evidence only; no character value or relationship is asserted." };
    }

    public TableNode WorldReferenceTable()
    {
        var catalog = references.Catalog(TsushimaIds.WorldGuide);
        return catalog is null
            ? new TableNode(GuideColumns(), []) { Title = "Shrine reference", Description = "No pinned reference guide is available." }
            : GuideTable(catalog);
    }

    public IReadOnlyList<ViewNode> ExpandGuide(string lazyId)
    {
        var catalogId = lazyId[TsushimaIds.GuidePrefix.Length..];
        if (catalogId is not (TsushimaIds.ResourceGuide or TsushimaIds.CharmGuide or TsushimaIds.ArmorGuide or
            TsushimaIds.SwordGuide or TsushimaIds.TechniqueGuide or TsushimaIds.WorldGuide))
        {
            throw new KeyNotFoundException($"Unknown reference guide '{lazyId}'.");
        }

        var catalog = references.Catalog(catalogId);
        if (catalog is null)
        {
            return [new TextNode("No pinned reference guide is available. Save identifiers remain unchanged.")];
        }

        // These curated guides fit one bounded page. Larger user packs cannot cause an eager unbounded view.
        var page = catalog.Search(new ReferenceQuery { Limit = GuidePageSize });
        return [new TextNode("Reference only. Names and acquisition information do not report owned, equipped, " +
            "discovered, completed or rewarded state. Wiki keys are not save IDs. " +
            $"Showing {page.Items.Count} of {catalog.Count} entries."), GuideTable(catalog)];
    }

    private TableNode GuideTable(IReferenceCatalog catalog)
    {
        var page = catalog.Search(new ReferenceQuery { Limit = GuidePageSize });
        var rows = page.Items.Select(record => new TableRow(record.Key, new Dictionary<string, ViewNode>
        {
            ["name"] = new TextNode(record.Name),
            ["category"] = new TextNode(record.Category ?? "Unknown"),
            ["description"] = new TextNode(record.Description ?? ""),
            ["state"] = new TextNode("Unmapped; save state Unknown") { Tone = Tone.Warning },
            ["source"] = new TextNode(record.AttributionText),
        })).ToArray();
        return new TableNode(GuideColumns(), rows)
        {
            Title = catalog.Dataset.Title,
            Description = catalog.Dataset.Attribution.ToShortText(),
        };
    }

    public static TableNode RawInventory(TsushimaInventory inventory, Func<int, uint> readWord)
    {
        var rows = inventory.Records.Select(record =>
        {
            var cells = new Dictionary<string, ViewNode> { ["name"] = new TextNode(record.Name) };
            for (var index = 0; index < InventoryRecord.WordCount; index++)
            {
                var offset = record.DataOffset + index * sizeof(uint);
                cells[$"word{index}"] = new TextNode($"0x{readWord(offset):X8} @ 0x{offset:X}") { Monospace = true };
            }

            return new TableRow(record.Name, cells);
        }).ToArray();
        return new TableNode([new TableColumn("name", "Record"),
            .. Enumerable.Range(0, InventoryRecord.WordCount).Select(i => new TableColumn($"word{i}", $"Word {i}"))], rows)
        { Description = "Unknown raw words. Edits invalidate this block so its snapshot can be refreshed." };
    }

    private FieldNode ReferenceField(string valueId, string rawId, string catalogId, IReferenceCatalog? catalog)
    {
        var record = catalog?.Find(rawId);
        return bind(new FieldNode(valueId, record?.Name ?? rawId, new ReferenceSpec(catalogId) { ReadOnly = true }, FieldValue.Of(rawId))
        {
            Confidence = MappingConfidence(record),
            Description = record is null ? "Raw identifier; no pinned name mapping." :
                $"{record.Description} Mapping: {record.Attributes.GetValueOrDefault("mappingEvidence", "Unspecified")}. {record.AttributionText}",
            Warning = "Read-only identifier: presence does not establish ownership, equipped state, or edit safety.",
        });
    }

    private static Confidence MappingConfidence(ReferenceRecord? record) =>
        record?.Attributes.GetValueOrDefault("mappingConfidence") switch
        {
            "Verified" => Confidence.Verified,
            "Inferred" => Confidence.Inferred,
            _ => Confidence.Unknown,
        };

    private static IReadOnlyList<QueryableColumn> IdentifierColumns() =>
    [
        new("raw", "Raw ID"), new("reference", "Reference"), new("category", "Category"),
        new("location", "First offset"), new("mapping", "Name mapping"), new("state", "Ownership / state"),
    ];

    private static IReadOnlyList<TableColumn> GuideColumns() =>
    [new("name", "Name"), new("category", "Category / region"), new("description", "Description"),
        new("state", "Save mapping / state"), new("source", "Attribution")];

    private TableRow IdentifierRow(TsushimaIdentifier identifier, ReferenceRecord? record, IReferenceCatalog? catalog)
    {
        return new TableRow(identifier.Name, new Dictionary<string, ViewNode>
        {
            ["raw"] = new TextNode(identifier.Name) { Monospace = true },
            ["reference"] = ReferenceField($"{TsushimaIds.Equipment}/{identifier.Name}/reference", identifier.Name,
                TsushimaIds.Equipment, catalog),
            ["category"] = new TextNode(record?.Category ?? "Unknown"),
            ["location"] = new TextNode($"0x{identifier.Offset:X}") { Monospace = true },
            ["mapping"] = new TextNode(MappingConfidence(record).ToString()),
            ["state"] = new TextNode("Unknown") { Tone = Tone.Warning },
        });
    }

    private static bool Matches(TsushimaIdentifier identifier, ReferenceRecord? record, QueryableCollectionQuery query)
    {
        if (query.Text is { Length: > 0 } text &&
            !identifier.Name.Contains(text, StringComparison.OrdinalIgnoreCase) &&
            !(record?.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        foreach (var (key, value) in query.Filters)
        {
            var actual = key switch
            {
                "raw" => identifier.Name,
                "reference" => record?.Name ?? identifier.Name,
                "category" => record?.Category ?? "Unknown",
                "mapping" => MappingConfidence(record).ToString(),
                "state" => "Unknown",
                _ => throw new ArgumentException($"Unknown equipment filter '{key}'.", nameof(query)),
            };
            if (!actual.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string SortValue(TsushimaIdentifier identifier, ReferenceRecord? record, string? column) => column switch
    {
        null or "raw" => identifier.Name,
        "reference" => record?.Name ?? identifier.Name,
        "category" => record?.Category ?? "Unknown",
        "location" => identifier.Offset.ToString("D10"),
        "mapping" => MappingConfidence(record).ToString(),
        "state" => "Unknown",
        _ => throw new ArgumentException($"Unknown equipment sort column '{column}'.", nameof(column)),
    };
}
