using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Read-only F9 views built from the attributed catalog; no save meaning is inferred.</summary>
internal sealed class AriseSpatialViews(AriseReferences references)
{
    private const string ProgressPath = "section:Recipe, fish and artifact guides";
    private const string MapSource = "https://aselia.fandom.com/wiki/Tales_of_Arise";
    private const string RepositorySource = "https://github.com/JKamsker/PS5SaveGame-Toolkit";
    private const double BackgroundWidth = 1200;
    private const double BackgroundHeight = 675;
    private const double SchematicSize = 100;
    private static readonly ReferenceAttribution SchematicAttribution = new(
        RepositorySource,
        "PS5 Save Game Toolkit contributors",
        "GPL-3.0-or-later",
        "https://www.gnu.org/licenses/gpl-3.0.html",
        new DateOnly(2026, 10, 28),
        "Original schematic positions generated for navigation, independently of the decorative background; no geographic alignment or game coordinates claimed.");

    private IReadOnlyList<ViewNode>? _graphs;
    private IReadOnlyList<ViewNode>? _maps;

    public IReadOnlyList<ViewNode> Graphs => _graphs ??= BuildGraphs();

    public IReadOnlyList<ViewNode> Maps => _maps ??= BuildMaps();

    private IReadOnlyList<ViewNode> BuildGraphs()
    {
        var titles = Records(AriseReferences.TitlesCatalogId).ToArray();
        var nodes = Records(AriseReferences.SkillNodesCatalogId).ToArray();
        var artes = Records(AriseReferences.ArtesCatalogId).ToArray();
        var sections = new List<ViewNode>();
        foreach (var character in AriseSchema.CharacterNames.Take(AriseSchema.MainCharacterCount))
        {
            var characterTitles = titles.Where(record => Attribute(record, "character") == character).ToArray();
            var characterNodes = nodes.Where(record => Attribute(record, "character") == character).ToArray();
            var characterArtes = artes.Where(record => Attribute(record, "character") == character).ToArray();
            sections.Add(new SectionNode($"{character} skill panels", [SkillGraph(character, characterTitles, characterNodes)]));
            sections.Add(new SectionNode($"{character} arte prerequisites", [ArteGraph(character, characterTitles, characterArtes)]));
        }

        return [
            new TextNode("Graphs show published title, skill-node and arte relationships only. Saved unlock, purchase, usage and proficiency state is Unknown; targets open the corresponding read-only catalog row.") { Tone = Tone.Warning },
            .. sections,
        ];
    }

    private GraphNode SkillGraph(string character, IReadOnlyList<ReferenceRecord> titles, IReadOnlyList<ReferenceRecord> nodes)
    {
        var items = new List<GraphItem>(titles.Count + nodes.Count);
        var edges = new List<GraphEdge>(nodes.Count);
        foreach (var title in titles.OrderBy(record => record.Key, StringComparer.Ordinal))
        {
            items.Add(new GraphItem(title.Key, title.Name, GraphState.Unknown)
            {
                Branch = "Title panel",
                Description = $"{title.Description} Saved unlock and completion state is Unknown. {title.AttributionText}",
                Target = ActionTarget("references/inspect/titles", "Artes and skills", "section:Title panels"),
            });
        }

        foreach (var node in nodes.OrderBy(record => record.Key, StringComparer.Ordinal))
        {
            var titleKey = Attribute(node, "titleKey");
            items.Add(new GraphItem(node.Key, node.Name, GraphState.Unknown)
            {
                Branch = Attribute(node, "title") ?? "Title skill",
                Cost = Attribute(node, "spCost"),
                Description = $"{node.Description} Saved purchase state is Unknown. {node.AttributionText}",
                Target = ActionTarget("references/inspect/skill-nodes", "Artes and skills", "section:Title skill nodes"),
            });
            if (titleKey is not null && titles.Any(title => title.Key == titleKey))
            {
                edges.Add(new GraphEdge(titleKey, node.Key));
            }
        }

        var alternative = $"{character} has {titles.Count} published title panels and {nodes.Count} published skill nodes. Each panel is a prerequisite group for its five nodes; saved unlock and purchase state is Unknown. Sources are attributed in each target row.";
        return new GraphNode($"{character} skill panels", items, edges, alternative);
    }

    private GraphNode ArteGraph(string character, IReadOnlyList<ReferenceRecord> titles, IReadOnlyList<ReferenceRecord> artes)
    {
        var items = new List<GraphItem>(titles.Count + artes.Count);
        var edges = new List<GraphEdge>();
        foreach (var title in titles.OrderBy(record => record.Key, StringComparer.Ordinal))
        {
            items.Add(new GraphItem(title.Key, title.Name, GraphState.Unknown)
            {
                Branch = "Title requirement",
                Description = $"Published title requirement node. Saved unlock state is Unknown. {title.AttributionText}",
                Target = ActionTarget("references/inspect/titles", "Artes and skills", "section:Title panels"),
            });
        }

        foreach (var arte in artes.OrderBy(record => record.Key, StringComparer.Ordinal))
        {
            var requirement = Attribute(arte, "requirements");
            items.Add(new GraphItem(arte.Key, arte.Name, GraphState.Unknown)
            {
                Branch = Attribute(arte, "category") ?? "Arte",
                Cost = Cost(arte.Description),
                Description = $"{arte.Description} Saved learned, usage and proficiency state is Unknown. {arte.AttributionText}",
                Target = ActionTarget("references/inspect/artes", "Artes and skills", "section:Artes"),
            });
            var title = titles.FirstOrDefault(candidate => requirement is not null &&
                requirement.Contains(candidate.Name, StringComparison.OrdinalIgnoreCase));
            if (title is not null)
            {
                edges.Add(new GraphEdge(title.Key, arte.Key));
            }
        }

        return new GraphNode($"{character} arte prerequisites", items, edges,
            $"Published arte requirements for {character}. Edges are added only when a published requirement contains an exact title name; saved learning, usage and proficiency state is Unknown.");
    }

    private IReadOnlyList<ViewNode> BuildMaps()
    {
        var records = Records(AriseReferences.ProgressCatalogId).ToArray();
        var markers = new List<MapMarker>(records.Length + 3);
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            var x = 6 + index % 12 * 8;
            var y = 6 + index / 12 * 8;
            markers.Add(new MapMarker(record.Key, record.Name, MapMarkerStatus.Unknown, record.Category ?? "Progress")
            {
                X = x,
                Y = y,
                Confidence = Confidence.Unknown,
                Description = $"Schematic category position only; no saved identity or completion state is established. {record.Description}",
                ExternalLink = record.Source is null ? null : new ExternalViewLink(record.Source, "Open attributed guide source", record.AttributionText),
                Target = RowTarget(record.Key, "Progress", ProgressPath),
            });
        }

        markers.AddRange([
            ExternalMarker("domain:owls", "Owls", "Collectible checklist; discovery, report and reward states are separate and Unknown.", "https://aselia.fandom.com/wiki/ToA_-_Owls", 6, 94),
            ExternalMarker("domain:camps", "Camps", "Fast-travel/camp unlock state is Unknown; no save join is claimed.", "https://aselia.fandom.com/wiki/ToA_-_Camps", 50, 94),
            ExternalMarker("domain:map-unlocks", "Map unlocks", "World/map unlock state is Unknown; no game coordinates are embedded.", MapSource, 94, 94),
        ]);

        return [
            new TextNode("Dahna artwork is a decorative background behind the original schematic index. Marker positions are not geographic locations or saved progress. The same schematic and text list remain available without the artwork; status and saved identity remain Unknown.") { Tone = Tone.Warning },
            new MapNode("Progress and location schematic", new MapBounds(0, 0, BackgroundWidth, BackgroundHeight), markers,
                "The schematic lists every pinned recipe, fish and artifact guide plus separate owl, camp and map domains. Background alignment is unconfirmed: markers retain category-grid positions, not geographic locations. All states remain Unknown.")
            {
                Background = references.ProgressBackground,
                Transform = new MapTransform(BackgroundWidth / SchematicSize, BackgroundHeight / SchematicSize, 0, 0),
                CoordinateSpace = "Plugin-authored schematic grid; category grouping only",
                CoordinateAttribution = SchematicAttribution,
                CoordinateEmbeddingPermitted = true,
                ExternalSource = new ExternalViewLink(MapSource, "Open external Tales of Arise map and guide index", "Aselia Wiki contributors, CC BY-SA 3.0. Background artwork has separate publisher attribution; no geographic coordinates imported."),
            },
        ];
    }

    private MapMarker ExternalMarker(string id, string label, string description, string url, double x, double y) =>
        new(id, label, MapMarkerStatus.Unknown, "Location domain")
        {
            X = x,
            Y = y,
            Confidence = Confidence.Unknown,
            Description = description,
            ExternalLink = new ExternalViewLink(url, "Open attributed external guide", "Aselia Wiki contributors, CC BY-SA 3.0; link only, no art or coordinates imported."),
        };

    private static ViewTarget RowTarget(string id, string tab, string section) => new(ViewTargetKind.Row, id)
    {
        Path = [$"tab:{tab}", section],
    };

    private static ViewTarget ActionTarget(string id, string tab, string section) => new(ViewTargetKind.Action, id)
    {
        Path = [$"tab:{tab}", section],
    };

    private static string? Attribute(ReferenceRecord record, string key) => record.Attributes.GetValueOrDefault(key);

    private static string? Cost(string? description)
    {
        if (description is null) return null;
        var line = description.Split('\n').FirstOrDefault(value => value.StartsWith("AG Cost:", StringComparison.Ordinal));
        return line ?? description.Split('\n').FirstOrDefault(value => value.StartsWith("CP Cost:", StringComparison.Ordinal));
    }

    private IEnumerable<ReferenceRecord> Records(string id)
    {
        var catalog = references.Catalog(id);
        if (catalog is null) return [];
        return All(catalog);
    }

    private static IEnumerable<ReferenceRecord> All(IReferenceCatalog catalog)
    {
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            foreach (var record in catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
            {
                yield return record;
            }
        }
    }
}
