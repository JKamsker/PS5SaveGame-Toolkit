namespace SaveToolkit.LocalApi.Protocol;

/// <summary>
/// A decoded query string with ASP.NET Core <c>IQueryCollection</c> semantics: keys are compared
/// case-insensitively, enumerate in first-occurrence order and keep every repeated value.
/// </summary>
internal interface IQueryValues
{
    IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> Entries { get; }

    bool TryGetValues(string key, out IReadOnlyList<string> values);
}

internal static class QueryValueExtensions
{
    public static bool ContainsKey(this IQueryValues query, string key) => query.TryGetValues(key, out _);

    /// <summary>The first value, like <c>Query[key].FirstOrDefault()</c>.</summary>
    public static string? First(this IQueryValues query, string key) =>
        query.TryGetValues(key, out var values) && values.Count > 0 ? values[0] : null;

    /// <summary>The string conversion of <c>StringValues</c>: null, the value, or comma-joined values.</summary>
    public static string? Joined(this IQueryValues query, string key) =>
        query.TryGetValues(key, out var values) ? Join(values) : null;

    public static string? Join(IReadOnlyList<string> values) => values.Count switch
    {
        0 => null,
        1 => values[0],
        _ => string.Join(",", values),
    };
}
