using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing.Diff;

/// <summary>
/// Decoder hook for semantic diffs: turns one save file into a bounded, flat list of typed values whose
/// <see cref="SemanticValue.Path"/> encodes the tree position with stable record identities
/// (<c>inventory[id=42]/count</c>, not <c>inventory[7]/count</c> or a byte offset). Two snapshots are then
/// aligned by path, so records that moved or were relocated still match.
/// Implementations must be read-only and must not keep state between calls.
/// </summary>
public interface ISaveDecoder
{
    /// <summary>Short id shown in reports, e.g. <c>json</c> or a plugin id.</summary>
    string Id { get; }

    /// <summary>Cheap check; must not throw for foreign data.</summary>
    bool CanDecode(ISaveFile file);

    /// <summary>Decodes the file. May throw (e.g. <see cref="Sdk.SaveFormatException"/>) for invalid data.</summary>
    SemanticTree Decode(ISaveFile file, DecodeLimits limits);
}

public sealed record DecodeLimits
{
    /// <summary>Values kept per file; the tree is marked truncated beyond this.</summary>
    public int MaxValues { get; init; } = 200_000;

    /// <summary>Characters kept per value; longer values are cut and suffixed with an ellipsis.</summary>
    public int MaxValueLength { get; init; } = 256;

    public int MaxDepth { get; init; } = 64;

    /// <summary>Files larger than this are not decoded.</summary>
    public long MaxFileBytes { get; init; } = 256L * 1024 * 1024;
}

/// <param name="Path">Identity path, '/'-separated; record identities in brackets.</param>
/// <param name="Kind">Value type, e.g. <c>integer</c>, <c>string</c>, <c>bool</c>.</param>
/// <param name="Value">Invariant text form of the value.</param>
public sealed record SemanticValue(string Path, string Kind, string Value)
{
    /// <summary>Optional human label (e.g. the plugin's field label).</summary>
    public string? Label { get; init; }
}

public sealed record SemanticTree(string Decoder, IReadOnlyList<SemanticValue> Values)
{
    /// <summary>True when <see cref="DecodeLimits"/> cut the tree; absent paths are then not evidence of removal.</summary>
    public bool Truncated { get; init; }

    /// <summary>Decoder notes such as duplicate identities or unexpanded sections.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Collects values for a <see cref="SemanticTree"/> while enforcing <see cref="DecodeLimits"/>. A path seen
/// twice keeps both values (the second becomes <c>path#2</c>) and records an identity-conflict warning.
/// </summary>
public sealed class SemanticTreeBuilder(string decoder, DecodeLimits limits)
{
    private const int MaxWarnings = 50;
    private readonly List<SemanticValue> _values = [];
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];
    private int _suppressedWarnings;

    public DecodeLimits Limits => limits;

    public bool Truncated { get; private set; }

    public bool Contains(string path) => _seen.ContainsKey(path);

    /// <summary>Adds a value; returns false once the value limit is reached (callers should stop walking).</summary>
    public bool Add(string path, string kind, string value, string? label = null)
    {
        if (_values.Count >= limits.MaxValues)
        {
            Truncated = true;
            return false;
        }

        if (_seen.TryGetValue(path, out var count))
        {
            _seen[path] = count + 1;
            Warn($"Duplicate identity '{path}' (occurrence {count + 1}); kept as '{path}#{count + 1}'.");
            path = $"{path}#{count + 1}";
        }
        else
        {
            _seen[path] = 1;
        }

        if (value.Length > limits.MaxValueLength)
        {
            value = string.Concat(value.AsSpan(0, limits.MaxValueLength), "…");
        }

        _values.Add(new SemanticValue(path, kind, value) { Label = label });
        return true;
    }

    public void Warn(string message)
    {
        if (_warnings.Count < MaxWarnings)
        {
            _warnings.Add(message);
        }
        else
        {
            _suppressedWarnings++;
        }
    }

    public void MarkTruncated(string reason)
    {
        Truncated = true;
        Warn(reason);
    }

    public SemanticTree Build()
    {
        var warnings = _suppressedWarnings > 0 ? [.. _warnings, $"{_suppressedWarnings} more warnings suppressed."] : _warnings.ToList();
        return new SemanticTree(decoder, _values.ToList()) { Truncated = Truncated, Warnings = warnings };
    }
}
