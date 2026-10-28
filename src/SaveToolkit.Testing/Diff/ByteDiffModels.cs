using System.Text.Json.Serialization;

namespace SaveToolkit.Testing.Diff;

public sealed record ByteDiffOptions
{
    /// <summary>Differences separated by at most this many equal bytes are reported as one range.</summary>
    public int MergeGap { get; init; } = 8;

    /// <summary>Ranges kept per file. Further ranges are still counted (see <see cref="ByteDiffResult.Truncated"/>).</summary>
    public int MaxRanges { get; init; } = 256;

    /// <summary>Equal bytes shown before and after each range in the hex preview.</summary>
    public int ContextBytes { get; init; } = 8;

    /// <summary>Changed bytes shown per range in the hex preview; longer ranges are cut.</summary>
    public int MaxPreviewBytes { get; init; } = 32;
}

[JsonConverter(typeof(JsonStringEnumConverter<ByteChangeKind>))]
public enum ByteChangeKind
{
    /// <summary>Bytes present on both sides that differ.</summary>
    Modified,

    /// <summary>Bytes only in the after file (it grew).</summary>
    Appended,

    /// <summary>Bytes only in the before file (it shrank).</summary>
    Truncated,
}

/// <summary>
/// One changed range. <see cref="Length"/> spans from the first to the last differing byte (equal bytes
/// inside merge gaps included); <see cref="DifferingBytes"/> counts only the bytes that really differ.
/// The hex windows start at <see cref="WindowStart"/> and include context bytes on both sides.
/// </summary>
public sealed record ByteChange(long Offset, long Length, ByteChangeKind Kind)
{
    public long DifferingBytes { get; init; }

    public long WindowStart { get; init; }

    public string? BeforeHex { get; init; }

    public string? AfterHex { get; init; }

    public long End => Offset + Length;
}

public sealed record ByteDiffResult
{
    public long BeforeLength { get; init; }

    public long AfterLength { get; init; }

    /// <summary>Differing bytes outside masked churn ranges, including length growth/shrinkage.</summary>
    public long ChangedBytes { get; init; }

    /// <summary>Differing bytes that fell inside masked churn ranges.</summary>
    public long IgnoredBytes { get; init; }

    /// <summary>Total number of ranges, also those beyond <see cref="ByteDiffOptions.MaxRanges"/>.</summary>
    public int RangeCount { get; init; }

    public IReadOnlyList<ByteChange> Ranges { get; init; } = [];

    public bool Truncated => RangeCount > Ranges.Count;

    public bool HasChanges => ChangedBytes > 0;
}
