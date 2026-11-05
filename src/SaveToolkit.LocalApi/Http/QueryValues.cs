using SaveToolkit.LocalApi.Protocol;

namespace SaveToolkit.LocalApi.Http;

/// <summary>Parses a raw query string exactly like ASP.NET Core's <c>QueryFeature</c>.</summary>
internal sealed class QueryValues : IQueryValues
{
    /// <summary>ASP.NET's small-dictionary threshold; beyond it a repeated key keeps its first spelling.</summary>
    private const int ArrayThreshold = 10;

    private readonly Dictionary<string, Entry> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Entry> _order = [];
    private bool _dictionaryMode;

    private QueryValues() { }

    public static QueryValues Empty { get; } = new();

    public IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> Entries =>
        _order.Select(entry => new KeyValuePair<string, IReadOnlyList<string>>(entry.Key, entry.Values));

    /// <param name="query">The raw query, with or without its leading '?'.</param>
    public static QueryValues Parse(string query)
    {
        var result = new QueryValues();
        var remaining = query.AsSpan();
        if (remaining.StartsWith("?")) remaining = remaining[1..];
        while (remaining.Length > 0)
        {
            ReadOnlySpan<char> segment;
            var delimiter = remaining.IndexOf('&');
            if (delimiter >= 0)
            {
                segment = remaining[..delimiter];
                remaining = remaining[(delimiter + 1)..];
            }
            else
            {
                segment = remaining;
                remaining = default;
            }

            var equals = segment.IndexOf('=');
            if (equals >= 0) result.Add(Decode(segment[..equals]), Decode(segment[(equals + 1)..]));
            else if (!segment.IsEmpty) result.Add(Decode(segment), string.Empty);
        }

        return result;
    }

    public bool TryGetValues(string key, out IReadOnlyList<string> values)
    {
        if (_values.TryGetValue(key, out var entry))
        {
            values = entry.Values;
            return true;
        }

        values = [];
        return false;
    }

    private void Add(string key, string value)
    {
        if (!_values.TryGetValue(key, out var entry))
        {
            _values[key] = entry = new Entry(key);
            _order.Add(entry);
            if (_order.Count > ArrayThreshold) _dictionaryMode = true;
        }
        else if (entry.Values.Count == 1 && !_dictionaryMode)
        {
            // ASP.NET's array-backed accumulator rewrites the whole entry on the second occurrence,
            // so a repeated key enumerates with that occurrence's spelling.
            entry.Key = key;
        }

        entry.Values.Add(value);
    }

    private sealed class Entry(string key)
    {
        public string Key { get; set; } = key;

        public List<string> Values { get; } = [];
    }

    private static string Decode(ReadOnlySpan<char> encoded) =>
        Uri.UnescapeDataString(encoded.ToString().Replace('+', ' '));
}
