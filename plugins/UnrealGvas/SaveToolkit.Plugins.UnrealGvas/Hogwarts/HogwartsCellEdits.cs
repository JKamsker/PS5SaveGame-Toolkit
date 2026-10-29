using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>Rebuilds an image from its imported bytes and the remaining cell changes.</summary>
internal sealed class HogwartsCellEdits(ReadOnlySpan<byte> original)
{
    private readonly byte[] _original = original.ToArray();
    private Dictionary<CellKey, CellChange> _changes = [];

    public bool HasChanges => _changes.Count > 0;

    public SqliteImage Apply(SqliteImage current, HogwartsTable table, long rowId, string column, object value)
    {
        var key = new CellKey(table.Name, rowId, column);
        string source = HogwartsTable.Quote(table.Name);
        string target = HogwartsTable.Quote(column);
        var parameters = new Dictionary<string, object?> { ["$rowid"] = rowId };
        var rows = current.Query($"SELECT {target} FROM {source} WHERE _rowid_ = $rowid", parameters).Rows;
        if (rows.Count != 1) throw new SaveFormatException("The selected Hogwarts row no longer exists.");
        object? before = rows[0][0];
        if (Equals(before, value)) return current;

        object? baseline = _changes.TryGetValue(key, out var prior) ? prior.Original : before;
        var changes = new Dictionary<CellKey, CellChange>(_changes);
        if (Equals(baseline, value)) changes.Remove(key);
        else changes[key] = new(baseline, value);

        // Replaying SQL inverses does not restore SQLite page encodings, allocation or counters.
        // A private rebuild also leaves the live image untouched when a statement fails.
        var candidate = SqliteImage.Open(_original, new SqliteLimits { MaxDatabaseBytes = HogwartsImage.MaxDecodedBytes });
        try
        {
            foreach (var (cell, change) in changes.OrderBy(pair => pair.Key.Table, StringComparer.Ordinal)
                         .ThenBy(pair => pair.Key.RowId).ThenBy(pair => pair.Key.Column, StringComparer.Ordinal))
            {
                string sql = $"UPDATE {HogwartsTable.Quote(cell.Table)} SET {HogwartsTable.Quote(cell.Column)} = $value WHERE _rowid_ = $rowid";
                if (candidate.Execute(sql, new Dictionary<string, object?> { ["$value"] = change.Value, ["$rowid"] = cell.RowId }) != 1)
                    throw new SaveFormatException("The selected Hogwarts row no longer exists.");
            }
            _changes = changes;
            return candidate;
        }
        catch { candidate.Dispose(); throw; }
    }

    private sealed record CellKey(string Table, long RowId, string Column);
    private sealed record CellChange(object? Original, object Value);
}
