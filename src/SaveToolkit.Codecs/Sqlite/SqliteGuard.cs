using SQLitePCL;

namespace SaveToolkit.Codecs.Sqlite;

// Connection-private authorization and work accounting. Never expose the raw connection, so callers
// cannot turn off memory-only settings, install extensions, or bypass result/VM limits.
internal sealed class SqliteGuard(SqliteLimits limits)
{
    private const int ProgressInterval = 1000;
    private const int MaxColumns = 256;
    private const int MaxExpressionDepth = 64;
    private const int MaxCompoundSelects = 32;
    private const int MaxVariables = 256;
    private const int MaxTriggerDepth = 16;
    private int _remaining;
    internal bool InternalSql { get; set; }
    internal bool QueryOnly { get; set; }

    internal void Install(sqlite3 handle)
    {
        Reset();
        raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_LENGTH, limits.MaxValueBytes);
        raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_SQL_LENGTH, limits.MaxSqlBytes);
        raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_COLUMN, MaxColumns);
        raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_EXPR_DEPTH, MaxExpressionDepth);
        raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_COMPOUND_SELECT, MaxCompoundSelects);
        raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_VARIABLE_NUMBER, MaxVariables);
        raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_ATTACHED, 0);
        raw.sqlite3_limit(handle, raw.SQLITE_LIMIT_TRIGGER_DEPTH, MaxTriggerDepth);
        raw.sqlite3_enable_load_extension(handle, 0);
        raw.sqlite3_progress_handler(handle, ProgressInterval, _ =>
        {
            _remaining -= ProgressInterval;
            return _remaining < 0 ? 1 : 0;
        }, this);
        strdelegate_authorizer authorizer = (_, action, first, second, database, _) => Authorize(action, first, second, database);
        if (raw.sqlite3_set_authorizer(handle, authorizer, this) != raw.SQLITE_OK)
            throw new InvalidDataException("Could not install SQLite authorization.");
    }

    internal void Reset() => _remaining = limits.MaxVmSteps;

    private int Authorize(int action, string? first, string? second, string? database)
    {
        if (InternalSql) return raw.SQLITE_OK;
        if (database is not null && database != "main") return raw.SQLITE_DENY;
        if (action is raw.SQLITE_ATTACH or raw.SQLITE_DETACH or raw.SQLITE_PRAGMA or
            raw.SQLITE_TRANSACTION or raw.SQLITE_SAVEPOINT or raw.SQLITE_CREATE_VTABLE or raw.SQLITE_DROP_VTABLE)
            return raw.SQLITE_DENY;
        if (action == raw.SQLITE_FUNCTION && string.Equals(second ?? first, "load_extension", StringComparison.OrdinalIgnoreCase))
            return raw.SQLITE_DENY;
        if (QueryOnly && action is not (raw.SQLITE_SELECT or raw.SQLITE_READ or raw.SQLITE_FUNCTION or raw.SQLITE_RECURSIVE))
            return raw.SQLITE_DENY;
        return raw.SQLITE_OK;
    }
}
