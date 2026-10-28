using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

internal sealed class HogwartsDocument : BoundSaveDocument, ISaveDocument, IQueryableDocument
{
    public const string CatalogId = "unrealgvas_hogwarts_collectibles";
    public const int MaxSlotBytes = 16 * 1024 * 1024;
    internal const string ReadOnlyReason = "Hogwarts metadata is read-only. The database schema is not available for a safe edit.";
    internal const string WriteWarning = "Not validated on console. Stored Kraken blocks and database roles are unproven; the game may reject or overwrite this edited save. Keep a backup.";
    private const string NavigationAttribution = "Navigation captions: PS5 Save Game Toolkit contributors (GPL-3.0-or-later), inferred from observed PS5 schema and MIT format research: https://github.com/producedbytmsh/hl-savetools/tree/e568dedcda2611f54cc793e6d0237c749ae25094. Collectible table roles: Malin001, MIT, https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/tree/02525dde0fd9bc0be72982478680377dccb41246.";
    private readonly string _path;
    private readonly GvasEnvelope _envelope;
    private readonly UnrealGvasDocument _raw;
    private readonly List<HogwartsDatabase> _databases = [];
    private readonly List<ViewNode> _warnings = [];
    private readonly Dictionary<string, HogwartsTable> _tables = new(StringComparer.Ordinal);
    private readonly IReferenceCatalog? _catalog;
    private bool _dirty;

    public HogwartsDocument(string path, GvasEnvelope envelope, IReferenceContext references)
    {
        _path = path;
        _envelope = envelope;
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

    public override string Title => "Hogwarts Legacy · database inspector";
    public override bool CanEdit => _databases.Count == 2 && _warnings.Count == 0;
    public IReadOnlyList<QueryableCollection> Collections { get; }

    public new FieldValue GetValue(string valueId) => valueId.StartsWith("property/", StringComparison.Ordinal)
        ? _raw.GetValue(valueId)
        : base.GetValue(valueId);
    FieldValue ISaveDocument.GetValue(string valueId) => GetValue(valueId);

    public override ViewNode BuildView()
    {
        var tabs = new List<Tab>
        {
            new("Hogwarts", [
                new TextNode(CanEdit ? WriteWarning : ReadOnlyReason) { Tone = Tone.Warning },
                new TextNode("RawDatabaseImage and RawExclusiveImage are inspected independently. No merge, authoritative-image or synchronization rule is assumed.") { Tone = Tone.Info },
                .. _databases.Select(database => (ViewNode)new StatsNode([
                    new Stat("Image", database.Name), new Stat("SQLite bytes", database.ImageBytes.ToString()),
                    new Stat("Tables", database.Tables.Count.ToString()), new Stat("SQLite integrity", "ok"),
                ])),
                .. _warnings,
                new TextNode($"Stored output is limited to {MaxSlotBytes / (1024 * 1024)} MiB until the console allocation is measured. Database cells retain their table and row identity; no cross-image synchronization is performed."),
                new TextNode("Collectible labels: " + (_catalog?.Dataset.Attribution.ToShortText() ?? "No catalog loaded; exact raw IDs are shown.")),
            ]),
        };
        foreach (string group in new[] { "Character", "Inventory", "Progression", "Collections" }) tabs.Add(new(group, Group(group)));
        tabs.Add(new("Database tables", [
            new TextNode("All tables are queryable. Filters match raw column text; search covers every raw column. Rowid fallback identities are stable only within this unchanged document."),
            .. _databases.Select(database => (ViewNode)SectionNode.Lazy(database.Name, "hogwarts/tables/" + database.Name)),
        ]));
        tabs.Add(new("GVAS properties", [_raw.BuildView()]));
        return new TabsNode(tabs);
    }

    private IReadOnlyList<ViewNode> Group(string group) =>
    [new TextNode("Read-only PS5 storage, grouped by table name (Inferred meaning). Both database images remain separate. Unknown IDs and relationships remain raw.") { Tone = Tone.Info },
        .. _tables.Values.Where(table => table.Group == group).Select(table => (ViewNode)TableNode(table))];

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        const string prefix = "hogwarts/tables/";
        if (!lazyId.StartsWith(prefix, StringComparison.Ordinal)) return _raw.ExpandNode(lazyId);
        var database = _databases.SingleOrDefault(database => prefix + database.Name == lazyId) ?? throw new KeyNotFoundException(lazyId);
        return database.Tables.Where(table => table.Group == "Database tables").Select(table => (ViewNode)TableNode(table)).ToArray();
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        if (!_tables.TryGetValue(collectionId, out var table)) throw new KeyNotFoundException(collectionId);
        var database = _databases.Single(database => database.Name == table.Image);
        var page = database.Query(table, query);
        var rows = page.Result.Rows.Select(row =>
        {
            string key = table.RowKey(row);
            long rowId = Convert.ToInt64(row[0], System.Globalization.CultureInfo.InvariantCulture);
            var cells = table.Columns.Select((column, index) => (column, value: row[index + 1]))
                .ToDictionary(cell => cell.column, cell => Cell(database, table, key, rowId, cell.column, cell.value), StringComparer.Ordinal);
            return new TableRow(key, cells);
        }).ToArray();
        return new(collectionId, Metadata(table).Columns, rows, table.Count, page.Filtered, page.Offset, page.Limit);
    }

    private ViewNode Cell(HogwartsDatabase database, HogwartsTable table, string rowKey, long rowId, string column, object? value)
    {
        string id = $"{table.Id}/{rowKey}/{column}";
        if (value is string key && _catalog?.Find(key) is { } record &&
            record.Attributes.GetValueOrDefault("table") == table.Name && record.Attributes.GetValueOrDefault("column") == column)
            return Field(id, column, new ReferenceSpec(CatalogId), () => FieldValue.Of(key)) with
            {
                Confidence = Confidence.Inferred,
                Description = record.AttributionText + ". " + record.Description,
                Warning = "Exact saved ID matched; location/state meaning has not been console-validated. Read-only catalog picker.",
            };
        if (!CanEdit || value is null or byte[] || table.IdentityColumns.Contains(column, StringComparer.Ordinal) || !IsWritableColumn(table.Name, column, value))
            return new TextNode(HogwartsTable.Text(value)) { Monospace = value is byte[] };
        return value switch
        {
            long number => EditableCell(database, table, id, rowId, column, number, IntegerSpec.Int64),
            double number => EditableCell(database, table, id, rowId, column, number, FloatSpec.Double),
            string text => EditableCell(database, table, id, rowId, column, text, new TextSpec { MaxLength = 1_048_576 }),
            _ => new TextNode(HogwartsTable.Text(value)),
        };
    }

    private static bool IsWritableColumn(string table, string column, object value)
    {
        if (table is not ("MiscDataDynamic" or "InventoryDynamic" or "GearItemsDynamic" or "EquippedGearDynamic" or "PlayerStatsDynamic")) return false;
        bool named = column.Contains("Count", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("Quantity", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("Knuts", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("Experience", StringComparison.OrdinalIgnoreCase) ||
            column.Contains("Level", StringComparison.OrdinalIgnoreCase) ||
            column is "XP" or "Value" or "State" or "Status" or "DataValue";
        return named && (value is long or double || value is string text && text.Length <= 256);
    }

    private FieldNode EditableCell<T>(HogwartsDatabase database, HogwartsTable table, string id, long rowId, string column, T value, FieldSpec spec)
    {
        T current = value;
        return Field(id, column, spec, () => ToFieldValue(current), next =>
        {
            T candidate = FromFieldValue<T>(next);
            if (EqualityComparer<T>.Default.Equals(current, candidate)) return;
            database.SetCell(table, rowId, column, candidate!);
            current = candidate;
            _dirty = true;
        }) with
        {
            Confidence = Confidence.Inferred,
            Description = $"Existing SQLite cell in {table.Image}/{table.Name}; table role and gameplay effect are unverified.",
            Warning = WriteWarning,
        };
    }

    private static QueryableCollection Metadata(HogwartsTable table) => new(table.Id, table.Image + " · " + table.Title,
        table.Columns.Select(column => new QueryableColumn(column, column)).ToArray(), table.Count)
    {
        Description = NavigationAttribution + $" {table.Name}; schema SHA256 {table.SchemaHash}. " +
            (table.IdentityColumns.Count == 0 ? "Rowid identity; cross-generation identity unproven." : "Observed unique identity: " + string.Join(" / ", table.IdentityColumns)),
    };

    private static QueryableTableNode TableNode(HogwartsTable table)
    {
        var metadata = Metadata(table);
        return new(metadata.Id, metadata.Title, metadata.Columns, metadata.TotalCount) { Description = metadata.Description };
    }

    public override IReadOnlyList<FileOutput> Serialize() => !_dirty
        ? _raw.Serialize()
        : [new FileOutput(_path, BuildEditedOutput())];

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        if (!CanEdit) return EditOutcome.Rejected("document", ReadOnlyReason);
        bool wasDirty = _dirty;
        var outcome = base.ApplyEdits(edits);
        if (!outcome.Succeeded || edits.Count == 0) return outcome;
        try { _ = BuildEditedOutput(); }
        catch (Exception error) when (error is SaveFormatException or InvalidDataException or ArgumentException or OverflowException)
        {
            foreach (var change in outcome.Changes.Reverse()) base.ApplyEdits([new ValueEdit(change.ValueId, change.OldValue)]);
            _dirty = wasDirty;
            return EditOutcome.Rejected(edits[^1].ValueId, error.Message);
        }
        return outcome with { Warnings = [WriteWarning] };
    }

    private byte[] BuildEditedOutput()
    {
        var replacements = new Dictionary<GvasNode, ReadOnlyMemory<byte>>();
        foreach (var database in _databases)
        {
            byte[] image = database.SerializeForWrite();
            byte[] frames = HogwartsImage.EncodeStored(image);
            byte[] payload = new byte[sizeof(int) + frames.Length];
            BinaryPrimitives.WriteInt32LittleEndian(payload, frames.Length);
            frames.CopyTo(payload.AsSpan(sizeof(int)));
            var node = _envelope.File.Properties.Single(candidate => candidate.Name == database.Name);
            replacements.Add(node, payload);
        }
        byte[] output = _envelope.File.SerializeReplacements(replacements);
        if (output.Length > MaxSlotBytes)
            throw new SaveFormatException($"Edited Hogwarts save is {output.Length:N0} bytes; the conservative {MaxSlotBytes:N0}-byte slot budget would be exceeded.");
        ValidateEditedOutput(output);
        return output;
    }

    private void ValidateEditedOutput(byte[] output)
    {
        var parsed = GvasFile.Parse(output);
        foreach (var database in _databases)
        {
            var node = parsed.Properties.Single(candidate => candidate.Name == database.Name);
            byte[] image = HogwartsImage.ReadArray(parsed, node);
            using var reopened = SqliteImage.Open(image);
            reopened.ValidateIntegrity();
        }
    }

    private static FieldValue ToFieldValue<T>(T value) => value switch
    {
        long number => FieldValue.Of(number), double number => FieldValue.Of(number), string text => FieldValue.Of(text),
        _ => throw new ArgumentException("Unsupported SQLite value type."),
    };

    private static T FromFieldValue<T>(FieldValue value) => typeof(T) switch
    {
        var type when type == typeof(long) => (T)(object)checked((long)value.AsInteger()),
        var type when type == typeof(double) => (T)(object)value.AsDouble(),
        var type when type == typeof(string) => (T)(object)value.AsString(),
        _ => throw new ArgumentException("Unsupported SQLite value type."),
    };

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        foreach (var database in _databases) database.Dispose();
        _raw.Dispose();
        base.Dispose(disposing);
    }
}
