using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace SaveToolkit.Codecs.Sqlite;

/// <summary>A private, bounded in-memory database image. No file or network I/O; not thread safe.</summary>
public sealed class SqliteImage : IDisposable
{
    private const string Schema = "main";
    private const int HeaderBytes = 100;
    private const int MaxParameters = 256;
    private readonly SqliteConnection _connection;
    private readonly SqliteLimits _limits;
    private readonly SqliteGuard _guard;
    private IntPtr _imageBuffer;
    private bool _disposed;

    private SqliteImage(ReadOnlySpan<byte> image, SqliteLimits limits)
    {
        limits.Validate();
        _limits = limits;
        _guard = new(limits);
        _connection = new("Data Source=:memory:;Mode=Memory;Cache=Private;Pooling=False");
        try
        {
            _connection.Open();
            _guard.Install(_connection.Handle!);
            if (!image.IsEmpty)
            {
                ValidateHeader(image, limits);
                // Keep ownership here (flags=0): SQLite cannot resize this fixed-capacity allocation.
                // Dispose closes the connection before freeing it, including every failure path.
                _imageBuffer = raw.sqlite3_malloc64(limits.MaxDatabaseBytes);
                if (_imageBuffer == IntPtr.Zero) throw new InvalidDataException("Could not allocate the SQLite image buffer.");
                Marshal.Copy(image.ToArray(), 0, _imageBuffer, image.Length);
                // SQLite internally authorizes its schema detach/attach while deserializing.
                _guard.InternalSql = true;
                int result;
                try
                {
                    result = raw.sqlite3_deserialize(_connection.Handle!, Schema, _imageBuffer,
                    image.Length, limits.MaxDatabaseBytes, 0);
                }
                finally { _guard.InternalSql = false; }
                if (result != raw.SQLITE_OK) throw new InvalidDataException($"SQLite deserialize failed ({result}).");
            }
            Internal("PRAGMA temp_store=MEMORY; PRAGMA journal_mode=MEMORY; PRAGMA trusted_schema=OFF;");
            long pageSize = Convert.ToInt64(InternalScalar("PRAGMA page_size"), CultureInfo.InvariantCulture);
            Internal(string.Create(CultureInfo.InvariantCulture, $"PRAGMA max_page_count={limits.MaxDatabaseBytes / pageSize};"));
            ValidateIntegrityCore();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static SqliteImage Open(ReadOnlySpan<byte> image, SqliteLimits? limits = null)
    {
        if (image.IsEmpty) throw new InvalidDataException("SQLite image is empty.");
        try
        {
            return new(image, limits ?? new());
        }
        catch (SqliteException ex)
        {
            throw new InvalidDataException($"Could not open SQLite database image: {ex.Message}", ex);
        }
    }

    public static SqliteImage Create(SqliteLimits? limits = null) => new([], limits ?? new());

    public IReadOnlyList<string> ListTables() => Query(
        "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
        .Rows.Select(row => (string)row[0]!).ToArray();

    /// <summary>Run read-only SQL, with optional named parameters. Limits apply to the complete result.</summary>
    public SqliteResult Query(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        CheckReady();
        _guard.Reset();
        _guard.QueryOnly = true;
        try
        {
            using var command = Command(sql, parameters);
            using var reader = command.ExecuteReader();
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            var rows = new List<IReadOnlyList<object?>>();
            long bytes = columns.Sum(c => (long)Encoding.UTF8.GetByteCount(c));
            if (bytes > _limits.MaxResultBytes) throw new InvalidDataException("SQLite result column-name limit exceeded.");
            while (reader.Read())
            {
                if (rows.Count == _limits.MaxRows) throw new InvalidDataException("SQLite result row limit exceeded.");
                var row = new object?[reader.FieldCount];
                for (int column = 0; column < row.Length; column++)
                {
                    object? value = reader.IsDBNull(column) ? null : reader.GetValue(column);
                    bytes += value switch { string text => Encoding.UTF8.GetByteCount(text), byte[] blob => blob.Length, _ => 8 };
                    if (bytes > _limits.MaxResultBytes) throw new InvalidDataException("SQLite result byte limit exceeded.");
                    row[column] = value;
                }
                rows.Add(Array.AsReadOnly(row));
            }
            if (reader.NextResult()) throw new InvalidDataException("Query accepts one result set.");
            return new(Array.AsReadOnly(columns), rows.AsReadOnly());
        }
        finally { _guard.QueryOnly = false; }
    }

    /// <summary>Execute trusted plugin SQL atomically; file operations and caller transactions are denied.</summary>
    public int Execute(string sql, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        CheckReady();
        _guard.Reset();
        // The transaction is internal; caller SQL cannot commit or roll it back.
        _guard.InternalSql = true;
        SqliteTransaction transaction;
        try { transaction = _connection.BeginTransaction(); }
        finally { _guard.InternalSql = false; }
        using var transactionScope = transaction;
        try
        {
            using var command = Command(sql, parameters);
            command.Transaction = transaction;
            int affected = command.ExecuteNonQuery();
            ValidateIntegrityCore();
            _guard.InternalSql = true;
            transaction.Commit();
            return affected;
        }
        catch
        {
            _guard.InternalSql = true;
            // SQLITE_FULL and SQLITE_INTERRUPT can cause SQLite to roll back automatically.
            if (raw.sqlite3_get_autocommit(_connection.Handle!) == 0) transaction.Rollback();
            throw;
        }
        finally { _guard.InternalSql = false; }
    }

    /// <summary>Validate and copy the current SQLite database bytes through sqlite3_serialize.</summary>
    public byte[] Serialize()
    {
        CheckReady();
        _guard.Reset();
        ValidateIntegrityCore();
        // sqlite3_serialize internally queries page_count/page_size through the authorizer.
        _guard.InternalSql = true;
        IntPtr serialized;
        long length;
        try { serialized = raw.sqlite3_serialize(_connection.Handle!, Schema, out length, 0); }
        finally { _guard.InternalSql = false; }
        try
        {
            if (serialized == IntPtr.Zero || length < HeaderBytes || length > _limits.MaxDatabaseBytes)
                throw new InvalidDataException("SQLite serialization failed or exceeds the image limit.");
            var bytes = new byte[checked((int)length)];
            Marshal.Copy(serialized, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { raw.sqlite3_free(serialized); }
    }

    public void ValidateIntegrity()
    {
        CheckReady();
        _guard.Reset();
        ValidateIntegrityCore();
    }

    // Enclosing operations share their work budget with the integrity check they require.
    private void ValidateIntegrityCore()
    {
        _guard.InternalSql = true;
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                throw new InvalidDataException("SQLite integrity_check failed.");
        }
        finally { _guard.InternalSql = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _connection.Dispose(); }
        finally
        {
            raw.sqlite3_free(_imageBuffer);
            _imageBuffer = IntPtr.Zero;
        }
    }

    private SqliteCommand Command(string sql, IReadOnlyDictionary<string, object?>? parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (Encoding.UTF8.GetByteCount(sql) > _limits.MaxSqlBytes) throw new InvalidDataException("SQLite SQL limit exceeded.");
        if (parameters is not null)
        {
            if (parameters.Count > MaxParameters) throw new InvalidDataException("SQLite parameter count limit exceeded.");
            foreach (var (key, value) in parameters)
            {
                int size = value switch { string text => Encoding.UTF8.GetByteCount(text), byte[] blob => blob.Length, _ => 8 };
                if (key.Length > MaxParameters || size > _limits.MaxValueBytes)
                    throw new InvalidDataException("SQLite parameter size limit exceeded.");
            }
        }
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        if (parameters is not null)
            foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }

    private object? InternalScalar(string sql)
    {
        _guard.InternalSql = true;
        try { using var command = Command(sql, null); return command.ExecuteScalar(); }
        finally { _guard.InternalSql = false; }
    }

    private void Internal(string sql)
    {
        _guard.InternalSql = true;
        try { using var command = Command(sql, null); command.ExecuteNonQuery(); }
        finally { _guard.InternalSql = false; }
    }

    private void CheckReady() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ValidateHeader(ReadOnlySpan<byte> image, SqliteLimits limits)
    {
        if (image.Length < HeaderBytes || image.Length > limits.MaxDatabaseBytes || !image[..16].SequenceEqual("SQLite format 3\0"u8))
            throw new InvalidDataException("Invalid SQLite database header or image size.");
        int pageSize = BinaryPrimitives.ReadUInt16BigEndian(image[16..]);
        if (pageSize == 1) pageSize = 65536;
        if (pageSize is < 512 or > 65536 || (pageSize & (pageSize - 1)) != 0 || image.Length % pageSize != 0)
            throw new InvalidDataException("Invalid SQLite page size or truncated image.");
        // WAL images need their separate WAL; silently treating them as standalone loses data.
        if (image[18] != 1 || image[19] != 1)
            throw new InvalidDataException("SQLite WAL database images are unsupported; supply a checkpointed image.");
    }
}
