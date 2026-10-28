using Microsoft.Data.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

internal sealed class HogwartsDocument : ISaveDocument, IQueryableDocument
{
    public const string CatalogId = "unrealgvas_hogwarts_collectibles";
    internal const string ReadOnlyReason = "Hogwarts is read-only. Database roles, game effects, stored-block slot budgets and console load/save/reload validation remain unverified.";
    private const string NavigationAttribution = "Navigation captions: PS5 Save Game Toolkit contributors (GPL-3.0-or-later), inferred from observed PS5 schema and MIT format research: https://github.com/producedbytmsh/hl-savetools/tree/e568dedcda2611f54cc793e6d0237c749ae25094. Collectible table roles: Malin001, MIT, https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/tree/02525dde0fd9bc0be72982478680377dccb41246.";
    private readonly UnrealGvasDocument _raw;
    private readonly List<HogwartsDatabase> _databases = [];
    private readonly List<ViewNode> _warnings = [];
    private readonly Dictionary<string, HogwartsTable> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FieldValue> _referenceValues = new(StringComparer.Ordinal);
    private readonly IReferenceCatalog? _catalog;

    public HogwartsDocument(string path, GvasEnvelope envelope, IReferenceContext references)
    {
        _raw = new(path, envelope, references, ReadOnlyReason);
        _catalog = references.Catalog(CatalogId);
        try
        {
            foreach (string name in new[] { "RawDatabaseImage", "RawExclusiveImage" })
            {
                var nodes = envelope.File.Properties.Where(node => node.Name == name).ToArray();
                if (nodes.Length != 1)
                {
                    _warnings.Add(new TextNode($"{name}: expected one database array; found {nodes.Length}. Overlay unavailable.") { Tone = Tone.Warning });
                    continue;
                }
                try
                {
                    var database = new HogwartsDatabase(name, HogwartsImage.ReadArray(envelope.File, nodes[0]));
                    _databases.Add(database);
                    foreach (var table in database.Tables) _tables.Add(table.Id, table);
                }
                catch (Exception error) when (error is SaveFormatException or InvalidDataException or SqliteException or ArgumentException or OverflowException)
                {
                    _warnings.Add(new TextNode($"{name}: database validation failed; overlay unavailable. {error.Message}") { Tone = Tone.Warning });
                }
            }
            Collections = _tables.Values.Select(Metadata).ToArray();
        }
        catch { Dispose(); throw; }
    }

    public string Title => "Hogwarts Legacy · database inspector";
    public bool CanEdit => false;
    public IReadOnlyList<QueryableCollection> Collections { get; }

    public ViewNode BuildView()
    {
        var tabs = new List<Tab>
        {
            new("Hogwarts", [
                new TextNode(ReadOnlyReason) { Tone = Tone.Warning },
                new TextNode("RawDatabaseImage and RawExclusiveImage are inspected independently. No merge, authoritative-image or synchronization rule is assumed.") { Tone = Tone.Info },
                .. _databases.Select(database => (ViewNode)new StatsNode([
                    new Stat("Image", database.Name), new Stat("SQLite bytes", database.ImageBytes.ToString()),
                    new Stat("Tables", database.Tables.Count.ToString()), new Stat("SQLite integrity", "ok"),
                ])),
                .. _warnings,
                new TextNode("Named groups describe observed storage. Collection states and challenge rewards are shown as saved; a flag is not proof of completion or reward receipt."),
                new TextNode("Collectible labels: " + (_catalog?.Dataset.Attribution.ToShortText() ?? "No catalog loaded; exact raw IDs are shown.")),
            ]),
        };
        foreach (string group in new[] { "Character", "Inventory", "Progression", "Collections" })
            tabs.Add(new(group, Group(group)));
        tabs.Add(new("Database tables", [
            new TextNode("All tables are queryable. Filters match raw column text; search covers every raw column. Rowid fallback identities are stable only within this unchanged document."),
            .. _databases.Select(database => (ViewNode)SectionNode.Lazy(database.Name, "hogwarts/tables/" + database.Name)),
        ]));
        tabs.Add(new("GVAS properties", [_raw.BuildView()]));
        return new TabsNode(tabs);
    }

    private IReadOnlyList<ViewNode> Group(string group) =>
    [
        new TextNode("Read-only PS5 storage, grouped by table name (Inferred meaning). Both database images remain separate. Unknown IDs and relationships remain raw.") { Tone = Tone.Info },
        .. _tables.Values.Where(table => table.Group == group).Select(table => (ViewNode)TableNode(table)),
    ];

    public IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        const string prefix = "hogwarts/tables/";
        if (!lazyId.StartsWith(prefix, StringComparison.Ordinal)) return _raw.ExpandNode(lazyId);
        var database = _databases.SingleOrDefault(database => prefix + database.Name == lazyId)
            ?? throw new KeyNotFoundException(lazyId);
        return database.Tables.Where(table => table.Group == "Database tables").Select(table => (ViewNode)TableNode(table)).ToArray();
    }

    private static QueryableCollection Metadata(HogwartsTable table) =>
        new(table.Id, table.Image + " · " + table.Title, table.Columns.Select(column => new QueryableColumn(column, column)).ToArray(), table.Count)
        {
            Description = NavigationAttribution + $" {table.Name}; schema SHA256 {table.SchemaHash}. " +
                (table.IdentityColumns.Count == 0 ? "Rowid identity; cross-generation identity unproven." : "Observed unique identity: " + string.Join(" / ", table.IdentityColumns)),
        };

    private static QueryableTableNode TableNode(HogwartsTable table)
    {
        var metadata = Metadata(table);
        return new(metadata.Id, metadata.Title, metadata.Columns, metadata.TotalCount) { Description = metadata.Description };
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        if (!_tables.TryGetValue(collectionId, out var table)) throw new KeyNotFoundException(collectionId);
        var database = _databases.Single(database => database.Name == table.Image);
        var page = database.Query(table, query);
        var rows = page.Result.Rows.Select(row =>
        {
            string key = table.RowKey(row);
            var cells = table.Columns.Select((column, index) => (column, value: row[index + 1]))
                .ToDictionary(cell => cell.column, cell => Cell(table, key, cell.column, cell.value), StringComparer.Ordinal);
            return new TableRow(key, cells);
        }).ToArray();
        return new(collectionId, Metadata(table).Columns, rows, table.Count, page.Filtered, page.Offset, page.Limit);
    }

    private ViewNode Cell(HogwartsTable table, string rowKey, string column, object? value)
    {
        if (value is string key && _catalog?.Find(key) is { } record &&
            record.Attributes.GetValueOrDefault("table") == table.Name && record.Attributes.GetValueOrDefault("column") == column)
        {
            string id = $"{table.Id}/{rowKey}/{column}";
            _referenceValues[id] = FieldValue.Of(key);
            return new FieldNode(id, column, new ReferenceSpec(CatalogId) { ReadOnly = true }, FieldValue.Of(key))
            {
                Confidence = Confidence.Inferred,
                Description = record.AttributionText + ". " + record.Description,
                Warning = "Exact saved ID matched; location/state meaning has not been console-validated. Read-only catalog picker.",
            };
        }
        string text = HogwartsTable.Text(value);
        // Bound wide, 200-row pages below the host response budget. Original values stay intact.
        int maxCharacters = Math.Min(256, 2048 / table.Columns.Count);
        if (text.Length > maxCharacters) text = text[..maxCharacters] + "… [SHA256 " + HogwartsTable.Hash(text)[..16] + "]";
        return new TextNode(text) { Monospace = value is byte[] };
    }

    public FieldValue GetValue(string valueId) => _referenceValues.TryGetValue(valueId, out var value) ? value : _raw.GetValue(valueId);
    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => new()
    {
        Errors = edits.Select(edit => new FieldError(edit.ValueId, ReadOnlyReason)).ToArray(),
    };
    public IReadOnlyList<FileOutput> Serialize() => _raw.Serialize();
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => _raw.ReadBlob(blobId, offset, count);
    public void Dispose()
    {
        foreach (var database in _databases) database.Dispose();
        _raw.Dispose();
    }
}
