using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>One independent, memory-only image. Only read methods escape to the document.</summary>
public sealed class HogwartsDatabase : IDisposable
{
    private const int MaxTables = 256;
    private const int MaxColumns = 64;
    private readonly SqliteImage _database;
    public IReadOnlyList<HogwartsTable> Tables { get; }
    public string Name { get; }
    public int ImageBytes { get; }

    public HogwartsDatabase(string name, ReadOnlySpan<byte> bytes)
    {
        Name = name;
        ImageBytes = bytes.Length;
        _database = SqliteImage.Open(bytes, new SqliteLimits { MaxDatabaseBytes = HogwartsImage.MaxDecodedBytes });
        try
        {
            var schemas = _database.Query("SELECT name, sql FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
            if (schemas.Rows.Count > MaxTables) throw new SaveFormatException("Hogwarts table count exceeds the safety limit.");
            Tables = schemas.Rows.Select(row => Describe((string)row[0]!, (string)row[1]!)).ToArray();
        }
        catch { _database.Dispose(); throw; }
    }

    private HogwartsTable Describe(string name, string sql)
    {
        string table = HogwartsTable.Quote(name);
        var columns = _database.Query($"SELECT * FROM {table} LIMIT 0").Columns;
        if (columns.Count is 0 or > MaxColumns || columns.Contains("rowid", StringComparer.OrdinalIgnoreCase) ||
            columns.Contains("_rowid_", StringComparer.OrdinalIgnoreCase) ||
            sql.Contains("WITHOUT ROWID", StringComparison.OrdinalIgnoreCase) ||
            sql.StartsWith("CREATE VIRTUAL", StringComparison.OrdinalIgnoreCase))
            throw new SaveFormatException("Hogwarts table schema is outside supported row/column bounds.");
        long count = (long)_database.Query($"SELECT count(*) FROM {table}").Rows[0][0]!;
        var identity = HogwartsTable.CandidateIdentity(name);
        if (identity.Any(column => !columns.Contains(column, StringComparer.Ordinal))) identity = [];
        if (identity.Count > 0)
        {
            string group = string.Join(",", identity.Select(HogwartsTable.Quote));
            // Null or duplicate tuples are not silently advertised as unique identities.
            string nulls = string.Join(" OR ", identity.Select(column => HogwartsTable.Quote(column) + " IS NULL"));
            if (_database.Query($"SELECT 1 FROM {table} WHERE {nulls} LIMIT 1").Rows.Count > 0 ||
                _database.Query($"SELECT 1 FROM {table} GROUP BY {group} HAVING count(*) > 1 LIMIT 1").Rows.Count > 0)
                identity = [];
        }
        return new(Name, name, columns, identity, count, HogwartsTable.Hash(sql));
    }

    public (SqliteResult Result, long Filtered, int Offset, int Limit) Query(HogwartsTable table, QueryableCollectionQuery query)
    {
        if (query.Cursor is not null) throw new ArgumentException("Hogwarts tables use offset paging, not cursors.");
        if (query.Offset < 0 || query.Limit < 1 || query.Limit > QueryableCollectionLimits.MaxPageSize ||
            query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount)
            throw new ArgumentException("Hogwarts table query exceeds paging/search limits.");
        if (query.SortColumn is { } sort && !table.Columns.Contains(sort, StringComparer.Ordinal))
            throw new ArgumentException("Unknown Hogwarts sort column.");
        var parameters = new Dictionary<string, object?> { ["$limit"] = query.Limit, ["$offset"] = query.Offset };
        var clauses = new List<string>();
        foreach (var (column, value) in query.Filters)
        {
            if (!table.Columns.Contains(column, StringComparer.Ordinal) || value.Length > QueryableCollectionLimits.MaxFilterLength)
                throw new ArgumentException("Unknown or oversized Hogwarts column filter.");
            string parameter = "$filter" + clauses.Count;
            // Exact textual representation, case-insensitive. NULL remains distinct from the string "NULL".
            clauses.Add($"CAST({HogwartsTable.Quote(column)} AS TEXT) = {parameter} COLLATE NOCASE");
            parameters[parameter] = value;
        }
        if (!string.IsNullOrEmpty(query.Text))
        {
            clauses.Add("(" + string.Join(" OR ", table.Columns.Select(column =>
                $"instr(lower(CAST({HogwartsTable.Quote(column)} AS TEXT)), lower($text)) > 0")) + ")");
            parameters["$text"] = query.Text;
        }
        string where = clauses.Count == 0 ? "" : " WHERE " + string.Join(" AND ", clauses);
        string source = HogwartsTable.Quote(table.Name);
        // A rowid tie-break guarantees stable pages even when the selected sort column repeats.
        string order = query.SortColumn is { } columnName
            ? HogwartsTable.Quote(columnName) + (query.Descending ? " DESC," : " ASC,") + "_rowid_"
            : "_rowid_";
        var countParameters = parameters.Where(pair => pair.Key is not ("$limit" or "$offset"))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        long filtered = where.Length == 0 ? table.Count :
            (long)_database.Query($"SELECT count(*) FROM {source}{where}", countParameters).Rows[0][0]!;
        var result = _database.Query($"SELECT _rowid_,* FROM {source}{where} ORDER BY {order} LIMIT $limit OFFSET $offset", parameters);
        return (result, filtered, query.Offset, query.Limit);
    }

    public void SetCell(HogwartsTable table, long rowId, string column, object value)
    {
        if (rowId < 1 || !table.Columns.Contains(column, StringComparer.Ordinal) || table.IdentityColumns.Contains(column, StringComparer.Ordinal))
            throw new ArgumentException("Only existing non-identity Hogwarts columns can be edited.");
        if (value is not (long or double or string)) throw new ArgumentException("Only SQLite integer, real and text cells can be edited.");
        string sql = $"UPDATE {HogwartsTable.Quote(table.Name)} SET {HogwartsTable.Quote(column)} = $value WHERE _rowid_ = $rowid";
        if (_database.Execute(sql, new Dictionary<string, object?> { ["$value"] = value, ["$rowid"] = rowId }) != 1)
            throw new SaveFormatException("The selected Hogwarts row no longer exists.");
    }

    /// <summary>Research only: SQLite enforces integrity_check before returning a no-op image.</summary>
    public byte[] SerializeForResearch() => _database.Serialize();
    /// <summary>Write path: serialize only after the shared codec's integrity check succeeds.</summary>
    public byte[] SerializeForWrite()
    {
        _database.ValidateIntegrity();
        return _database.Serialize();
    }
    public void Dispose() => _database.Dispose();
}
