using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Evidence index only. Unknown ownership, prerequisites and locations stay unknown.</summary>
internal sealed class SpatialViews(RecordIndex index, InventoryState state,
    Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind)
{
    internal const string MapUrl = "https://atomicheart.fandom.com/wiki/Locations";
    internal const string SkillsUrl = "https://atomicheart.fandom.com/wiki/Skills";
    private const string DetailsTitle = "Saved skill and weapon records";
    private const int SummaryLength = 900;
    private sealed record Entry(string Id, Func<string> Label, string Branch, string Description, ByteSpan Span, Func<string> Value, int Page);
    private readonly IReadOnlyList<Entry> entries = Entries(index, state);

    internal IReadOnlyList<ViewNode> BuildView() => [
        new TextNode("Saved skill collections and weapon references are inspection evidence. Ownership, upgrade levels, costs and prerequisites are unknown; no prerequisite edges or unlocked states are inferred. Community guides are external links only.") { Tone = Tone.Warning },
        SectionNode.Lazy("Skills", "spatial/skills/0"),
        SectionNode.Lazy("Weapon upgrades", "spatial/weapons/0"),
        SectionNode.Lazy(DetailsTitle, "spatial/detail-pages/0"),
        SectionNode.Lazy("Locations and level records", "spatial/map/0"),
    ];

    internal IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        var parts = lazyId.Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[2], out var start) || start < 0) throw new KeyNotFoundException(lazyId);
        if (parts[1] is "skills" or "weapons")
        {
            var branch = parts[1] == "skills" ? "Skills" : "Weapon upgrades";
            var source = entries.Where(e => e.Branch == branch).ToArray();
            if (start != 0 && start >= source.Length) throw new KeyNotFoundException(lazyId);
            var nodes = Graphs(branch, source.Skip(start).Take(AtomicHeartDocument.PageSize).ToArray()).ToList();
            More(nodes, start, source.Length, parts[1]);
            return nodes;
        }
        if (parts[1] == "detail-pages")
        {
            var count = (entries.Count + AtomicHeartDocument.PageSize - 1) / AtomicHeartDocument.PageSize;
            if (start != 0 && start >= count) throw new KeyNotFoundException(lazyId);
            var nodes = Enumerable.Range(start, Math.Min(AtomicHeartDocument.PageSize, Math.Max(0, count - start)))
                .Select(page => (ViewNode)SectionNode.Lazy($"Saved evidence {page * AtomicHeartDocument.PageSize + 1}", $"spatial/details/{page * AtomicHeartDocument.PageSize}")).ToList();
            More(nodes, start, count, "detail-pages");
            return nodes;
        }
        if (parts[1] == "details" && (start == 0 || start < entries.Count))
        {
            var nodes = entries.Skip(start).Take(AtomicHeartDocument.PageSize).Select(e => (ViewNode)new SectionNode(e.Label(), [
                bind(e.Id, e.Label(), new TextSpec(), () => FieldValue.Of(e.Value())) with {
                    Confidence = Confidence.Unknown, Description = e.Description,
                    Warning = "Serialized record only; skill ownership, upgrade level and prerequisites are unverified.",
                }, RecordViews.Hex(e.Span, "Original serialized evidence"),
            ])).ToList();
            return nodes;
        }
        if (parts[1] == "map" && (start == 0 || start < index.Levels.Count))
        {
            var markers = index.Levels.Skip(start).Take(AtomicHeartDocument.PageSize).Select((level, offset) => new MapMarker(
                level.Id, Short(level.Name), MapMarkerStatus.Unknown, Region(level.Name))
            {
                Confidence = Confidence.Unknown,
                Description = $"Saved level identifier: {Clip(level.Name)}. Serialized actors: {level.ActorCount}. Presence is not discovery, completion or fast-travel availability. No geographical coordinates are inferred.",
                ExternalLink = new(MapUrl, "Community location guide", "Atomic Heart Wiki contributors; link only, no text, coordinates or artwork imported."),
                Target = new(ViewTargetKind.Row, level.Id) { Path = ["tab:Record explorer", "section:Levels", $"section:inspect/levels/{(start + offset) / AtomicHeartDocument.PageSize * AtomicHeartDocument.PageSize}"] },
            }).ToArray();
            var map = new MapNode("Saved levels · location index", new(0, 0, 1, 1), markers,
                "All level statuses are unknown. The list links to saved level records and an external community guide; no art, coordinates, completion or prerequisite data is imported.")
            {
                CoordinateSpace = "External guide only; no coordinates",
                ExternalSource = new(MapUrl, "Open community locations", "Atomic Heart Wiki contributors; reuse terms do not establish permission for map art or coordinates. Link only."),
            };
            var nodes = new List<ViewNode> { map };
            More(nodes, start, index.Levels.Count, "map");
            return nodes;
        }
        throw new KeyNotFoundException(lazyId);
    }

    private IReadOnlyList<ViewNode> Graphs(string title, IReadOnlyList<Entry> source)
    {
        if (source.Count == 0) return [new TextNode($"{title}: no mapped serialized entries in this save. This does not prove absence of owned skills or upgrades.")];
        var items = source.Select(e => new GraphItem(e.Id, e.Label(), GraphState.Unknown)
        {
            Branch = e.Branch,
            Description = e.Description,
            Target = new(ViewTargetKind.Field, e.Id) { Path = DetailPath(e) },
        }).ToArray();
        return [new GraphNode(title + " · serialized evidence", items, [],
                "All states and prerequisites are unknown. No edges mean no prerequisite mapping has been established, not that there are no prerequisites. Each entry links to its saved evidence and original bytes.")];
    }

    private string[] DetailPath(Entry entry)
    {
        var page = entry.Page;
        var path = new List<string> { "tab:Skills and world" };
        for (var start = 0; start <= page / AtomicHeartDocument.PageSize; start += AtomicHeartDocument.PageSize)
            path.Add($"section:spatial/detail-pages/{start}");
        path.Add($"section:spatial/details/{page}");
        return path.ToArray();
    }
    private static void More(List<ViewNode> nodes, int start, int total, string kind)
    {
        if (start + AtomicHeartDocument.PageSize < total) nodes.Add(SectionNode.Lazy("More", $"spatial/{kind}/{start + AtomicHeartDocument.PageSize}"));
    }

    private static IReadOnlyList<Entry> Entries(RecordIndex index, InventoryState state)
    {
        var result = new List<Entry>();
        foreach (var record in index.Objects.Where(r => r.ClassPath.EndsWith("/BP_DefaultPlayerSkillTreeComponent.BP_DefaultPlayerSkillTreeComponent_C", StringComparison.Ordinal)))
        {
            var occurrences = new Dictionary<(string, int), int>();
            foreach (var property in record.Properties.Where(p => p.Type is "SetProperty" or "MapProperty"))
            {
                var identity = (property.Name, property.ArrayIndex);
                var occurrence = occurrences.GetValueOrDefault(identity);
                occurrences[identity] = occurrence + 1;
                var empty = property.Payload.Length == 2 * sizeof(int) && index.Plain.AsSpan(property.Payload.Offset, property.Payload.Length).IndexOfAnyExcept((byte)0) < 0;
                var description = $"{Clip(record.Name)}/{Clip(property.Name)}: {property.Type} {Clip(property.Detail)}; " +
                    (empty ? "two serialized zero counts (empty collection in this snapshot). " : "opaque collection retained without guessing its entries. ") +
                    $"Not a confirmed ownership or prerequisite mapping. Community skills reference (link only): {SkillsUrl}.";
                result.Add(new(RecordIdentity.Compact("skill", $"{record.Id}/{property.Name}[{property.ArrayIndex}]\0{occurrence}"), () => Clip(property.Name), "Skills", description, property.Payload,
                    () => empty ? "Empty serialized collection; gameplay state unknown" : "Opaque serialized collection; gameplay state unknown", Page(result.Count)));
            }
        }
        foreach (var item in state.Items.Where(i => InventoryState.Populated(i) &&
            (state.Catalog?.Find(InventoryState.ShortId(i.RawAsset))?.Category == "weapon" || i.RawAsset.Contains("/Weapons/", StringComparison.Ordinal))))
        {
            var reference = state.Catalog?.Find(InventoryState.ShortId(item.RawAsset));
            result.Add(new(RecordIdentity.Compact("weapon", item.Key), () => Clip(state.Catalog?.Find(InventoryState.ShortId(state.Asset(item)))?.Name ?? InventoryLabels.Name(InventoryState.ShortId(state.Asset(item)))), "Weapon upgrades",
                $"Existing saved weapon reference; upgrade/module levels, ownership and prerequisites are unknown. {Clip(reference?.AttributionText ?? "Original toolkit identifier formatting; source is the save's ItemDataAsset path. Weapon category inferred from its path.")}",
                item.Element.Tag, () => state.Asset(item), Page(result.Count)));
        }
        return result;
    }

    private static int Page(int ordinal) => ordinal / AtomicHeartDocument.PageSize * AtomicHeartDocument.PageSize;
    private static string Clip(string text) => text.Length > SummaryLength ? text[..SummaryLength] + "…" : text;
    private static string Short(string path) => Clip(path[(path.LastIndexOf('/') + 1)..]);
    private static string Region(string path)
    {
        var parts = path.Split('/');
        var world = Array.IndexOf(parts, "Atomic_World_01");
        return world >= 0 && world + 2 < parts.Length ? Clip(parts[world + 2]) : "Other saved levels";
    }
}
