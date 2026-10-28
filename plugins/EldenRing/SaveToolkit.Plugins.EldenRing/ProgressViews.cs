using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class ProgressViews(byte[] bytes, IReadOnlyList<SemanticCharacter> characters, IReferenceCatalog? catalog)
{
    public const string TabTitle = "World / progress";
    public const string MapUrl = "https://mapgenie.io/elden-ring/maps/the-lands-between";
    private readonly Lazy<ProgressIndex> _index = new(() => ProgressReferences.Read(catalog));
    private static IReadOnlyList<QueryableColumn> Columns =>
    [new("name", "Source candidate", new ReferenceSpec(ProgressReferences.CatalogId) { ReadOnly = true }),
        new("category", "Category"), new("region", "Source region"), new("event", "Source event ID"),
        new("raw", "Raw source bit (not verified status)"), new("details", "Evidence / uncertainty")];

    public IReadOnlyList<QueryableCollection> Collections => characters.Select(character => new QueryableCollection(
        CollectionId(character.Slot), $"Slot {character.Slot} · progress candidates", Columns,
        _index.Value.Regions.Sum(region => region.Entries.Count)) { Description = _index.Value.Notice }).ToArray();

    public bool OwnsCollection(string id) => characters.Any(character => id == CollectionId(character.Slot));

    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind)
    {
        var character = characters.Single(character => id == CollectionId(character.Slot));
        var range = character.Ranges.Single(range => range.Name == SemanticLayout.EventRangeName);
        var entries = _index.Value.Regions.SelectMany(region => region.Entries).ToArray();
        var result = BrowseQuery.Apply(entries, query, Columns, entry => entry.Key,
            (entry, column) => Text(entry, range, column), (entry, column) => column == "event" ? entry.EventId : null);
        return new(id, Columns, result.Page.Select(entry => Row(character.Slot, entry, range, bind)).ToArray(),
            entries.Length, result.Filtered, result.Offset, result.Limit);
    }

    public IReadOnlyList<ViewNode> Root() =>
    [
        new TextNode("Optional spoiler content: named base-game grace, boss and map-control candidates. Expand a slot and region to inspect. "
            + ProgressReferences.Warning) { Tone = Tone.Warning },
        .. Collections.Select(collection => new QueryableTableNode(collection.Id, collection.Title, collection.Columns,
            collection.TotalCount) { Description = collection.Description }),
        .. characters.Select(character => SectionNode.Lazy($"Slot {character.Slot} · progress candidates (spoilers)", SlotId(character.Slot))),
    ];

    public IReadOnlyList<ViewNode> Expand(string lazyId, Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind)
    {
        foreach (var character in characters)
        {
            if (lazyId == SlotId(character.Slot))
                return [new TextNode(_index.Value.Notice) { Tone = Tone.Warning },
                    .. _index.Value.Regions.Select(region => SectionNode.Lazy(region.Title, RegionId(character.Slot, region),
                        region.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)))];
            if (!lazyId.StartsWith(SlotId(character.Slot) + "/", StringComparison.Ordinal)) continue;
            foreach (var region in _index.Value.Regions)
                if (lazyId == RegionId(character.Slot, region)) return Region(character, region, bind);
        }
        throw new KeyNotFoundException($"Unknown progress section '{lazyId}'.");
    }

    private IReadOnlyList<ViewNode> Region(SemanticCharacter character, ProgressRegion region,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind)
    {
        var range = character.Ranges.Single(range => range.Name == SemanticLayout.EventRangeName);
        var path = new[] { "tab:" + TabTitle, "section:" + SlotId(character.Slot), "section:" + RegionId(character.Slot, region) };
        var markers = region.Entries.Select(entry => new MapMarker(entry.RowId(character.Slot), entry.Reference.Name,
            MapMarkerStatus.Unknown, entry.Category)
        {
            Confidence = Confidence.Inferred,
            Description = MarkerDescription(entry, range),
            Target = new(ViewTargetKind.Field, entry.ValueId(character.Slot)) { Path = [.. path, "row:" + entry.RowId(character.Slot)] },
        }).ToArray();
        return
        [
            new MapNode(region.Title + " (external map; unpositioned candidates)", new(0, 0, 1, 1), markers,
                $"{region.Entries.Count} named source candidates. All statuses Unknown; the table exposes the raw source bit and provenance. "
                + "No coordinates/art imported, no geographic placement, completion count or quest prerequisites inferred.")
            {
                CoordinateSpace = "External reference only; no embedded coordinates",
                ExternalSource = new(MapUrl, "Open community map (Map Genie)",
                    "Map Genie contributors. Link only; reuse licence unverified. No art or coordinates imported."),
            },
            new TableNode(Columns.Select(column => new TableColumn(column.Key, column.Label)).ToArray(),
                region.Entries.Select(entry => Row(character.Slot, entry, range, bind)).ToArray())
            { Title = region.Title + " · raw candidates", Description = ProgressReferences.Warning },
        ];
    }

    private TableRow Row(int slot, ProgressEntry entry, SaveRange range,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind) => new(entry.RowId(slot), new Dictionary<string, ViewNode>
    {
        ["name"] = bind(entry.RowId(slot) + "/reference", entry.Reference.Name, new ReferenceSpec(ProgressReferences.CatalogId),
            () => FieldValue.Of(entry.Key)) with { Confidence = Confidence.Inferred, Description = ReferenceBrowser.Details(entry.Reference) },
        ["category"] = new TextNode(entry.Category), ["region"] = new TextNode(entry.Region),
        ["event"] = new TextNode(entry.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["raw"] = bind(entry.ValueId(slot), entry.Reference.Name + " · raw source bit", new BoolSpec(),
            () => FieldValue.Of(Read(bytes, range, entry.Offset, entry.Bit))) with
        { Confidence = Confidence.Inferred, Description = Description(entry, range), Warning = ProgressReferences.Warning },
        ["details"] = new SectionNode("Progress candidate evidence", [new TextNode(Description(entry, range))]),
    });

    private string Text(ProgressEntry entry, SaveRange range, string column) => column switch
    {
        "name" => entry.Reference.Name + " " + entry.Key,
        "category" => entry.Category, "region" => entry.Region,
        "event" => entry.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "raw" => Read(bytes, range, entry.Offset, entry.Bit) ? "set" : "unset",
        "details" => Description(entry, range),
        _ => throw new ArgumentException("Unknown progress column."),
    };

    private string Description(ProgressEntry entry, SaveRange range) =>
        $"Source event {entry.EventId}, relative offset 0x{entry.Offset:X}, LSB bit {entry.Bit}: "
        + (Read(bytes, range, entry.Offset, entry.Bit) ? "set" : "unset") + ". "
        + ProgressReferences.Warning + " " + ReferenceBrowser.Details(entry.Reference);

    private string MarkerDescription(ProgressEntry entry, SaveRange range)
    {
        const string fullEvidence = " … Full evidence in the linked table row.";
        var description = Description(entry, range);
        if (description.Length <= SpatialViewLimits.MaxTextLength) return description;
        var length = SpatialViewLimits.MaxTextLength - fullEvidence.Length;
        if (char.IsHighSurrogate(description[length - 1])) length--;
        return description[..length] + fullEvidence;
    }

    internal static bool Read(ReadOnlySpan<byte> source, SaveRange range, int offset, int bit)
    {
        if (range.Offset < 0 || range.Length < 0 || range.Offset > source.Length - range.Length
            || offset < 0 || offset >= range.Length || bit is < 0 or >= ProgressReferences.BitsPerByte)
            throw new SaveToolkit.Sdk.SaveFormatException("Progress candidate bit is outside the bounded event range.");
        return (source[range.Offset + offset] & (1 << bit)) != 0;
    }

    private static string SlotId(int slot) => $"progress/{slot}";
    private static string CollectionId(int slot) => $"slots/{slot}/progress";
    private static string RegionId(int slot, ProgressRegion region) => $"{SlotId(slot)}/{region.Id}";
}
