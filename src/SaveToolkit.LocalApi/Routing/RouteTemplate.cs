namespace SaveToolkit.LocalApi.Routing;

/// <summary>A route template of literal and <c>{parameter}</c> segments, matched like ASP.NET Core routing.</summary>
internal sealed class RouteTemplate
{
    private readonly Segment[] _segments;

    public RouteTemplate(string template)
    {
        Text = template;
        _segments = [.. template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Segment.Parse)];
    }

    public string Text { get; }

    /// <summary>Matches decoded path segments (case-insensitive literals; parameters need a nonempty segment).</summary>
    public bool TryMatch(IReadOnlyList<string> path, Dictionary<string, string> values)
    {
        if (path.Count != _segments.Length) return false;
        for (var i = 0; i < _segments.Length; i++)
        {
            var segment = _segments[i];
            if (segment.Parameter is null)
            {
                if (!string.Equals(segment.Literal, path[i], StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (path[i].Length == 0)
            {
                return false;
            }
        }

        for (var i = 0; i < _segments.Length; i++)
            if (_segments[i].Parameter is { } name) values[name] = path[i];
        return true;
    }

    /// <summary>Orders by route precedence: at the first differing segment a literal beats a parameter.</summary>
    public static int ComparePrecedence(RouteTemplate left, RouteTemplate right)
    {
        var count = Math.Min(left._segments.Length, right._segments.Length);
        for (var i = 0; i < count; i++)
        {
            var l = left._segments[i].Parameter is null ? 0 : 1;
            var r = right._segments[i].Parameter is null ? 0 : 1;
            if (l != r) return l.CompareTo(r);
        }

        return left._segments.Length.CompareTo(right._segments.Length);
    }

    /// <summary>Splits a decoded path into segments; a trailing slash is ignored like ASP.NET's path tokenizer.</summary>
    public static string[] SplitPath(string path)
    {
        var trimmed = path.StartsWith('/') ? path[1..] : path;
        if (trimmed.EndsWith('/')) trimmed = trimmed[..^1];
        return trimmed.Length == 0 ? [] : trimmed.Split('/');
    }

    private sealed record Segment(string Literal, string? Parameter)
    {
        public static Segment Parse(string text) =>
            text.StartsWith('{') && text.EndsWith('}') ? new Segment(text, text[1..^1]) : new Segment(text, null);
    }
}
