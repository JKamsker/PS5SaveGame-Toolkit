using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using System.Security.Cryptography;
using System.Text;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class MapViews
{
    public const string CollectionId = "saved/map-tags";
    private const int DetailPageSize = 64;
    public const string SourceUrl = "https://legacy.witcher3map.com/";
    private const string SourceAttribution = "untamed0 and witcher3map contributors, CC BY-NC-SA; game art belongs to CD PROJEKT RED. " +
        "External link only; no artwork, coordinates or community marker data imported.";
    private static IReadOnlyList<QueryableColumn> Columns => [new("id", "Native map tag"), new("state", "Stored map membership"), new("details", "Details")];
    private readonly IReadOnlyList<SavedMapTag>? _tags;
    private readonly FieldBinding _bind;
    private readonly string? _warning;

    public MapViews(Sav3Tree tree, byte[] data, IReadOnlyList<string> names, FieldBinding bind)
    {
        _bind = bind;
        try { _tags = MapReader.Read(tree, data, names); }
        catch (SaveFormatException ex) { _warning = $"Map reader unavailable: {ex.Message}"; }
    }

    public QueryableCollection Collection => new(CollectionId, "Saved map tags", Columns, _tags?.Count ?? 0);

    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Read-only native map tags. Discovered means membership in DiscoveredMapPinTags, not completion or a Place-of-Power reward. " +
            "Absent tags are unknown, never missing. Region/name/coordinate joins require matching definitions. " + SourceAttribution),
        .. (_tags is not null ? Array.Empty<ViewNode>() : [new TextNode(_warning ??
            "No supported common map-tag blocks were decoded; an empty list does not imply no discoveries.") { Tone = Tone.Warning }]),
        new QueryableTableNode(CollectionId, Collection.Title, Columns, Collection.TotalCount),
        .. Enumerable.Range(0, Math.Max(1, ((_tags?.Count ?? 0) + SpatialViewLimits.MaxItems - 1) / SpatialViewLimits.MaxItems))
            .Select(page => SectionNode.Lazy($"Map tag list · {page + 1}", $"map-view/{page}")),
        .. Enumerable.Range(0, Math.Max(1, ((_tags?.Count ?? 0) + DetailPageSize - 1) / DetailPageSize))
            .Select(page => SectionNode.Lazy($"Map tag linked details · {page + 1}", $"map-details/{page * DetailPageSize}")),
    ];

    public IReadOnlyList<ViewNode> Expand(string id)
    {
        var parts = id.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var page) || page < 0) { throw new KeyNotFoundException(id); }
        var tags = _tags ?? [];
        if (parts[0] == "map-view" && page < Math.Max(1, (tags.Count + SpatialViewLimits.MaxItems - 1) / SpatialViewLimits.MaxItems))
        {
            return [new MapNode("Saved map tags", new MapBounds(0, 0, 1, 1), tags.Skip(page * SpatialViewLimits.MaxItems)
                .Take(SpatialViewLimits.MaxItems).Select((tag, index) => new MapMarker(tag.Id, tag.Id,
                    tag.Discovered ? MapMarkerStatus.Found : MapMarkerStatus.Unknown, tag.Disabled ? "Disabled saved tag" : "Saved map tag") {
                    Confidence = Confidence.Inferred, Description = State(tag) + ". Source: native CCommonMapManager counted blocks. " +
                        "Discovered is not completed; no reward, fast-travel availability or region is inferred.",
                    Target = Target(tag, page * SpatialViewLimits.MaxItems + index),
                    ExternalLink = new(SourceUrl, "Community regional maps", SourceAttribution),
                }).ToArray(), "Native map-tag memberships only. Coordinates and region joins are unknown. " +
                    "The text list and links remain available without a background. Missing tags are not treated as undiscovered locations.") {
                    CoordinateSpace = "Unmapped native tags; external maps only", ExternalSource = new(SourceUrl, "Community regional maps", SourceAttribution),
                }];
        }
        if (parts[0] == "map-details" && page <= tags.Count)
        {
            return [Table(tags.Skip(page).Take(DetailPageSize))];
        }
        throw new KeyNotFoundException(id);
    }

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null || query.Offset < 0 || query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount || query.Filters.Any(f => f.Key is not ("id" or "state") ||
                f.Value.Length > QueryableCollectionLimits.MaxFilterLength) || query.SortColumn is not (null or "id" or "state" or "details"))
        {
            throw new ArgumentException("Unsupported map-tag query.");
        }
        var matches = (_tags ?? []).Where(t => (string.IsNullOrWhiteSpace(query.Text) || $"{t.Id} {State(t)}".Contains(query.Text, StringComparison.OrdinalIgnoreCase)) &&
            query.Filters.All(f => string.IsNullOrEmpty(f.Value) || string.Equals(f.Key == "id" ? t.Id : State(t), f.Value, StringComparison.OrdinalIgnoreCase)));
        Func<SavedMapTag, string> key = query.SortColumn == "state" ? State : t => t.Id;
        var sorted = (query.Descending ? matches.OrderByDescending(key, StringComparer.Ordinal) : matches.OrderBy(key, StringComparer.Ordinal))
            .ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new(CollectionId, Columns, Table(sorted.Skip(query.Offset).Take(limit)).Rows, _tags?.Count ?? 0, sorted.Length, query.Offset, limit);
    }

    private TableNode Table(IEnumerable<SavedMapTag> tags) => new([new("id", "Native map tag"), new("state", "Stored map membership"), new("details", "Interpretation")],
        tags.Select(t => new TableRow(t.Id, new Dictionary<string, ViewNode> {
            ["id"] = new TextNode(t.Id), ["state"] = _bind(ValueId(t), "Stored map membership", new TextSpec(), () => FieldValue.Of(State(t))) with {
                Confidence = Confidence.Inferred, Description = "Source: native map-tag blocks. Discovery does not establish completion, availability or rewards.",
            }, ["details"] = new TextNode("Known, discovered and disabled are separate memberships; no write or coordinates are provided."),
        })).ToArray());

    private static string ValueId(SavedMapTag tag) => $"map/{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag.Id)))}/state";
    private static string State(SavedMapTag tag) => $"Known={tag.Known}; Discovered={tag.Discovered}; Disabled={tag.Disabled}";
    private static ViewTarget Target(SavedMapTag tag, int index)
    {
        return new(ViewTargetKind.Field, ValueId(tag)) { Path = ["tab:Map tags",
            $"section:map-details/{index / DetailPageSize * DetailPageSize}", $"row:{tag.Id}"] };
    }
}
