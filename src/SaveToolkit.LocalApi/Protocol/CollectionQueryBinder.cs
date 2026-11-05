using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>Binds and bounds the collection query string; violations are 400 transport errors.</summary>
internal static class CollectionQueryBinder
{
    private const int MaxFilterKeyLength = 128;

    public static (QueryableCollectionQuery Query, long? Revision) Bind(IQueryValues request)
    {
        var (offset, limit) = ReadPaging(request);
        if (!TryBool(request, "descending", false, out var descending)
            || !TryBool(request, "includeEmpty", false, out var includeEmpty)
            || !TryLong(request, "revision", out var revision))
        {
            throw new ApiStatusException("Descending, includeEmpty and revision must be valid values.");
        }

        var filters = ParseFilters(request)
            ?? throw new ApiStatusException("Filters must be a JSON object of string values.");

        var query = new QueryableCollectionQuery
        {
            Text = request.First("q") ?? request.First("text"),
            Filters = filters,
            SortColumn = request.First("sort") is { Length: > 0 } sort ? sort : null,
            Descending = descending,
            IncludeEmpty = request.ContainsKey("includeEmpty") ? includeEmpty : null,
            Offset = offset,
            Limit = limit,
            Cursor = request.First("cursor"),
        };
        if (query.Text is { Length: > QueryableCollectionLimits.MaxQueryLength }
            || query.Cursor is { Length: 0 or > QueryableCollectionLimits.MaxCursorLength }
            || query.Cursor is not null && offset != 0
            || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount
            || query.Filters.Any(f => f.Key.Length == 0 || f.Key.Length > MaxFilterKeyLength || f.Value.Length > QueryableCollectionLimits.MaxFilterLength))
        {
            throw new ApiStatusException("Collection query exceeds the protocol limits.");
        }

        // Apply clearing after all overrides and bounds checks, preserving nonempty/whitespace values.
        foreach (var key in filters.Where(filter => filter.Value.Length == 0).Select(filter => filter.Key).ToArray())
            filters.Remove(key);

        return (query, revision);
    }

    private static (int Offset, int Limit) ReadPaging(IQueryValues request)
    {
        if (!TryInt(request, "offset", 0, out var offset) || !TryInt(request, "limit", QueryableCollectionLimits.DefaultPageSize, out var limit))
        {
            throw new ApiStatusException("Offset and limit must be integers.");
        }

        if (limit is < 1 or > QueryableCollectionLimits.MaxPageSize)
        {
            throw new ApiStatusException($"Page size must be between 1 and {QueryableCollectionLimits.MaxPageSize}.");
        }

        if (offset < 0)
        {
            throw new ApiStatusException("Offset cannot be negative.");
        }

        return (offset, limit);
    }

    private static Dictionary<string, string>? ParseFilters(IQueryValues request)
    {
        var filters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.First("filters") is { Length: > 0 } json)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, ToolkitJson.Options);
                if (parsed is null || parsed.Values.Any(value => value is null)) return null;
                foreach (var (key, value) in parsed) filters[key] = value;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        foreach (var item in request.Entries.Where(p => p.Key.StartsWith("filter.", StringComparison.OrdinalIgnoreCase)))
        {
            var key = item.Key["filter.".Length..];
            if (key.Length == 0 || item.Value.Count == 0) return null;
            filters[key] = item.Value[^1];
        }

        return filters;
    }

    private static bool TryInt(IQueryValues request, string name, int fallback, out int value)
    {
        if (!request.TryGetValues(name, out var raw))
        {
            value = fallback;
            return true;
        }

        return int.TryParse(QueryValueExtensions.Join(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryLong(IQueryValues request, string name, out long? value)
    {
        value = null;
        if (!request.TryGetValues(name, out var raw)) return true;
        if (!long.TryParse(QueryValueExtensions.Join(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static bool TryBool(IQueryValues request, string name, bool fallback, out bool value)
    {
        if (!request.TryGetValues(name, out var raw))
        {
            value = fallback;
            return true;
        }

        return bool.TryParse(QueryValueExtensions.Join(raw), out value);
    }
}
