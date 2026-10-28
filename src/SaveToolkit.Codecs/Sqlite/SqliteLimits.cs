namespace SaveToolkit.Codecs.Sqlite;

/// <summary>Per-image limits. Instances are validated when a database is opened.</summary>
public sealed record SqliteLimits
{
    public int MaxDatabaseBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxRows { get; init; } = 1000;
    public int MaxResultBytes { get; init; } = 8 * 1024 * 1024;
    public int MaxValueBytes { get; init; } = 1024 * 1024;
    public int MaxSqlBytes { get; init; } = 64 * 1024;
    public int MaxVmSteps { get; init; } = 10_000_000;

    internal void Validate()
    {
        if (MaxDatabaseBytes is < 4096 or > OodleCodec.MaximumBytes || MaxRows is < 1 or > 100_000 ||
            MaxResultBytes is < 1 or > OodleCodec.MaximumBytes || MaxValueBytes < 1 || MaxValueBytes > MaxResultBytes ||
            MaxSqlBytes is < 1 or > 1024 * 1024 || MaxVmSteps is < 1000 or > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(SqliteLimits), "Invalid SQLite resource limits.");
    }
}
