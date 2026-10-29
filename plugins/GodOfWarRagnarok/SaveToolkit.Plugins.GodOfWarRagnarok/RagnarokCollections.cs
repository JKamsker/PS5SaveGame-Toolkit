using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed record RagnarokCollectionRow(string Key, int Offset, Dictionary<string, string> Cells,
    string? NumberColumn = null, int NumberOffset = 0, Confidence Confidence = Confidence.Unknown)
{
    // Each collection has either a metadata word or a fixed diagnostic word. Quantities stay live.
    public ulong FixedNumber { get; } = ulong.Parse(Cells.GetValueOrDefault("metadata") ?? Cells.GetValueOrDefault("word") ?? "0",
        CultureInfo.InvariantCulture);
}

/// <summary>Query stable row identities with live counter values; bind only the requested page.</summary>
internal sealed class RagnarokCollections
{
    private readonly RagnarokReferences _references;
    private readonly Dictionary<string, (QueryableCollection Info, IReadOnlyList<RagnarokCollectionRow> Rows)> _collections = new(StringComparer.Ordinal);

    public RagnarokCollections(RagnarokDocument document, RagnarokRecordIndex index, RagnarokReferences references)
    {
        _references = references;
        if (index.Preferences)
        {
            return;
        }

        var (resources, ids, words) = RagnarokCollectionRows.Wallets(document, index, references);
        Add("resources", "Resources · all counted containers", resources, "Quantity and container roles are inferred. Duplicate IDs in separate containers are never summed.");
        Add("xp", "XP · attributed role candidates", resources.Where(r => r.Cells["category"] == "XP").ToArray(),
            "PC runtime XP labels are candidates; PS5 character ownership and XP/level dependencies need controlled captures.");
        Add("collectibles", "Collectibles · raven resource counters", resources.Where(r => r.Cells["category"] == "collectible").ToArray(),
            "PC raven counter identities match PS5 rows. Values are raw counters, without a derived completion total.");
        Add("nested-ids", "Nested IDs · gear/skill candidates", ids,
            "Counted IDs are structurally bounded. Gear, skill, ownership and equipped-slot meanings remain Unknown.");
        Add("index-words", "Nested index-like words", words,
            "Counted u32 values. Early saves disprove zero-based references into the neighboring ID list; targets remain Unknown.");

        var flags = RagnarokCollectionRows.Flags(document, index, references);
        Add("flags", "Progression · grouped flags", flags,
            "Four raw bytes per ID. Matching PC skill-tree group names are inferred; individual skill IDs, unlock bits and quest/collectible completion remain Unknown.");

        var directory = RagnarokCollectionRows.Directory(document, index, references);
        Add("directory", "Sorted directory IDs", directory, "Paired-word semantics remain Unknown.");
    }

    public IReadOnlyList<QueryableCollection> Metadata => _collections.Values.Select(c => c.Info).ToArray();

    public QueryableTableNode View(string id)
    {
        var info = _collections[id].Info;
        return new(info.Id, info.Title, info.Columns, info.TotalCount) { Description = info.Description };
    }

    public QueryableCollectionPage Query(RagnarokDocument document, string id, QueryableCollectionQuery query)
    {
        if (!_collections.TryGetValue(id, out var collection))
        {
            throw new KeyNotFoundException($"Unknown Ragnarök collection '{id}'.");
        }

        if (query.Cursor is not null)
        {
            throw new ArgumentException("Ragnarök collections use offset paging; cursors are unsupported.", nameof(query));
        }

        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(filter => filter.Value.Length > QueryableCollectionLimits.MaxFilterLength))
        {
            throw new ArgumentException("Ragnarök collection query exceeds the supported bounds.", nameof(query));
        }

        var keys = collection.Info.Columns.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        if (query.Filters.Keys.Any(key => !keys.Contains(key)) || query.SortColumn is { } sort && !keys.Contains(sort))
        {
            throw new ArgumentException("Unknown Ragnarök collection column.", nameof(query));
        }

        var matches = RagnarokCollectionQuery.Match(document, collection.Rows, query);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var rows = matches.Skip(offset).Take(limit).Select(row => Render(document, row)).ToArray();
        return new(id, collection.Info.Columns, rows, collection.Info.TotalCount, matches.Count, offset, limit);
    }

    private void Add(string id, string title, IReadOnlyList<RagnarokCollectionRow> rows, string description)
    {
        // Empty lists still publish their schema, allowing stable filtered views on early saves.
        var columns = rows.FirstOrDefault()?.Cells.Keys.ToArray() ?? id switch
        {
            "nested-ids" => ["container", "id", "name", "category", "source", "offset"],
            "index-words" => ["container", "word", "offset"],
            "flags" => ["group", "id", "group-name", "group-source", "name", "source", "flags", "offset"],
            "directory" => ["id", "name", "source", "word", "offset"],
            _ => ["container", "name", "id", "category", "quantity", "metadata", "source", "offset", "icon"],
        };
        if (id is "resources" or "xp" or "collectibles") columns = [.. columns, "inspect", "restore"];
        var info = new QueryableCollection(id, title, columns.Select(key => new QueryableColumn(key, Label(key))).ToArray(), rows.Count)
        { Description = columns.Contains("container", StringComparer.Ordinal) ? description + " " + RagnarokReferences.ContainerSources : description };
        _collections.Add(id, (info, rows));
    }

    private TableRow Render(RagnarokDocument document, RagnarokCollectionRow row)
    {
        var cells = row.Cells.ToDictionary(pair => pair.Key, pair => (ViewNode)new TextNode(pair.Value), StringComparer.Ordinal);
        if (row.NumberColumn is { } column)
        {
            cells[column] = document.CounterField(row.NumberOffset);
            cells["inspect"] = RagnarokInspection.Action(row.Offset);
            cells["restore"] = RagnarokWrites.RestoreAction();
        }

        if (cells.ContainsKey("id"))
        {
            var id = document.ReadId(row.Offset);
            if (_references.Catalog(id) is { } catalog)
                cells["id"] = document.ResourceReference(row.Offset, catalog, _references.Name(id));

            if (_references.Find(id)?.AssetId is { } asset)
                cells["icon"] = new ImageNode(asset, _references.Name(id));
        }

        return new(row.Key, cells);
    }

    private static string Label(string key) => key switch
    {
        "id" => "ID (literal LE bytes)", "quantity" => "Quantity (inferred)", "metadata" => "Metadata (unknown)",
        "word" => "Word (unknown)", "flags" => "Four raw bytes", "source" => "Attribution", "container" => "Container (role inferred)",
        "group-name" => "Group role (inferred)", "group-source" => "Group attribution",
        _ => char.ToUpperInvariant(key[0]) + key[1..],
    };

}
