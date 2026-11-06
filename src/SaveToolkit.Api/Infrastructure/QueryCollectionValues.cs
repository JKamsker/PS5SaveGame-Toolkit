using SaveToolkit.LocalApi.Protocol;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Exposes ASP.NET's parsed query to the shared, transport-neutral binders.</summary>
internal sealed class QueryCollectionValues(IQueryCollection query) : IQueryValues
{
    public IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> Entries =>
        query.Select(pair => new KeyValuePair<string, IReadOnlyList<string>>(pair.Key, Values(pair.Value)));

    public bool TryGetValues(string key, out IReadOnlyList<string> values)
    {
        if (query.TryGetValue(key, out var raw))
        {
            values = Values(raw);
            return true;
        }

        values = [];
        return false;
    }

    private static string[] Values(Microsoft.Extensions.Primitives.StringValues raw) =>
        [.. raw.Select(value => value ?? string.Empty)];
}
