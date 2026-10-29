using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed record RagnarokCollectionRow(string Key, int Offset, IReadOnlyDictionary<string, string> Cells,
    string? NumberColumn = null, int NumberOffset = 0, Confidence Confidence = Confidence.Unknown);

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

        var resources = new List<RagnarokCollectionRow>();
        var ids = new List<RagnarokCollectionRow>();
        var words = new List<RagnarokCollectionRow>();
        var wallets = index.Nested.Wallets.Count > 0 ? index.Nested.Wallets :
            [new RagnarokWallet(RagnarokFormat.HeroWalletId, index.WalletOffset, index.WalletCount, 0, 0)];
        foreach (var wallet in wallets)
        {
            AddResources(document, references, wallet, resources);
            for (var row = 0; row < wallet.IdCount; row++)
            {
                var offset = wallet.IdsOffset + row * sizeof(ulong);
                var id = document.ReadId(offset);
                ids.Add(new($"wallet/{wallet.Id:X16}/ids/{row}", offset, Cells(
                    ("container", references.Container(wallet.Id)), ("id", RagnarokReferences.Key(id)),
                    ("name", references.Name(id)), ("category", references.Find(id)?.Category ?? "Unknown"),
                    ("source", references.Attribution(id)), ("offset", Hex(offset)))));
            }

            for (var row = 0; row < wallet.IndexCount; row++)
            {
                var offset = wallet.IndicesOffset + row * sizeof(uint);
                words.Add(new($"wallet/{wallet.Id:X16}/index-like/{row}", offset, Cells(
                    ("container", references.Container(wallet.Id)), ("word", document.ReadWord(offset).ToString(CultureInfo.InvariantCulture)),
                    ("offset", Hex(offset)))));
            }
        }

        Add("resources", "Resources · all counted containers", resources, "Quantity and container roles are inferred. Duplicate IDs in separate containers are never summed.");
        Add("xp", "XP · attributed role candidates", resources.Where(r => r.Cells["category"] == "XP").ToArray(),
            "PC runtime XP labels are candidates; PS5 character ownership and XP/level dependencies need controlled captures.");
        Add("collectibles", "Collectibles · raven resource counters", resources.Where(r => r.Cells["category"] == "collectible").ToArray(),
            "PC raven counter identities match PS5 rows. Values are raw counters, without a derived completion total.");
        Add("nested-ids", "Nested IDs · gear/skill candidates", ids,
            "Counted IDs are structurally bounded. Gear, skill, ownership and equipped-slot meanings remain Unknown.");
        Add("index-words", "Nested index-like words", words,
            "Counted u32 values. Early saves disprove zero-based references into the neighboring ID list; targets remain Unknown.");

        var flags = new List<RagnarokCollectionRow>();
        foreach (var group in index.Nested.FlagGroups)
        {
            for (var row = 0; row < group.Count; row++)
            {
                var offset = group.RowsOffset + row * RagnarokFlagGroup.RowSize;
                var id = document.ReadId(offset);
                flags.Add(new($"flags/{group.Id:X16}/{id:X16}/{row}", offset, Cells(
                    ("group", RagnarokReferences.Key(group.Id)), ("id", RagnarokReferences.Key(id)),
                    ("group-name", references.Community.Tree(group.Id)?.Name ?? "Unknown group"),
                    ("group-source", references.Community.Tree(group.Id)?.AttributionText ?? "No sourced group mapping"),
                    ("name", references.Name(id)), ("source", references.Attribution(id)),
                    ("flags", $"0x{document.ReadWord(offset + sizeof(ulong)):X8}"), ("offset", Hex(offset)))));
            }
        }

        Add("flags", "Progression · grouped flags", flags,
            "Four raw bytes per ID. Matching PC skill-tree group names are inferred; individual skill IDs, unlock bits and quest/collectible completion remain Unknown.");

        var directory = Enumerable.Range(0, index.DirectoryCount).Select(row =>
        {
            var offset = RagnarokFormat.DirectoryItemsOffset + row * sizeof(ulong);
            var id = document.ReadId(offset);
            return new RagnarokCollectionRow($"directory/{id:X16}", offset, Cells(
                ("id", RagnarokReferences.Key(id)), ("name", references.Name(id)),
                ("source", references.Attribution(id)), ("word", document.ReadWord(index.FlagsOffset + row * sizeof(uint)).ToString(CultureInfo.InvariantCulture)),
                ("offset", Hex(offset))));
        }).ToArray();
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

        var matches = collection.Rows.Select(row => LiveRow(document, row)).Where(row =>
            (string.IsNullOrWhiteSpace(query.Text) || row.Cells.Values.Any(value => value.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase))) &&
            query.Filters.All(filter => string.Equals(row.Cells.GetValueOrDefault(filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase))).ToArray();
        var sorted = query.SortColumn is { } column
            ? query.Descending ? matches.OrderByDescending(row => SortValue(row, column), StringComparer.OrdinalIgnoreCase)
                : matches.OrderBy(row => SortValue(row, column), StringComparer.OrdinalIgnoreCase)
            : matches.OrderBy(row => row.Offset);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var rows = sorted.ThenBy(row => row.Key, StringComparer.Ordinal).Skip(offset).Take(limit).Select(row => Render(document, row)).ToArray();
        return new(id, collection.Info.Columns, rows, collection.Info.TotalCount, matches.Length, offset, limit);
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

    private static void AddResources(RagnarokDocument document, RagnarokReferences references, RagnarokWallet wallet, List<RagnarokCollectionRow> rows)
    {
        var occurrences = new Dictionary<ulong, int>();
        for (var row = 0; row < wallet.ResourceCount; row++)
        {
            var offset = wallet.ResourcesOffset + row * RagnarokFormat.WalletRowSize;
            var id = document.ReadId(offset);
            var occurrence = occurrences.GetValueOrDefault(id);
            occurrences[id] = occurrence + 1;
            rows.Add(new($"wallet/{wallet.Id:X16}/resource/{id:X16}/{occurrence}", offset, Cells(
                ("container", references.Container(wallet.Id)), ("name", references.Name(id)), ("id", RagnarokReferences.Key(id)),
                ("category", references.Category(id)), ("quantity", document.ReadWord(offset + sizeof(ulong)).ToString(CultureInfo.InvariantCulture)),
                ("metadata", document.ReadWord(offset + RagnarokFormat.WalletRowMetadataOffset).ToString(CultureInfo.InvariantCulture)),
                ("source", references.Attribution(id)), ("offset", Hex(offset)), ("icon", string.Empty)),
                "quantity", offset + sizeof(ulong), Confidence.Inferred));
        }
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

    private static RagnarokCollectionRow LiveRow(RagnarokDocument document, RagnarokCollectionRow row)
    {
        if (row.NumberColumn is not { } column) return row;
        var cells = row.Cells.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        cells[column] = document.ReadWord(row.NumberOffset).ToString(CultureInfo.InvariantCulture);
        return row with { Cells = cells };
    }

    private static string SortValue(RagnarokCollectionRow row, string key) =>
        key is "quantity" or "metadata" or "word" && ulong.TryParse(row.Cells[key], out var number)
            ? number.ToString("D20", CultureInfo.InvariantCulture) : row.Cells.GetValueOrDefault(key) ?? "";

    private static string Hex(int offset) => $"0x{offset:X8}";
    private static string Label(string key) => key switch
    {
        "id" => "ID (literal LE bytes)", "quantity" => "Quantity (inferred)", "metadata" => "Metadata (unknown)",
        "word" => "Word (unknown)", "flags" => "Four raw bytes", "source" => "Attribution", "container" => "Container (role inferred)",
        "group-name" => "Group role (inferred)", "group-source" => "Group attribution",
        _ => char.ToUpperInvariant(key[0]) + key[1..],
    };

    private static IReadOnlyDictionary<string, string> Cells(params (string Key, string Value)[] cells) =>
        cells.ToDictionary(cell => cell.Key, cell => cell.Value, StringComparer.Ordinal);
}
