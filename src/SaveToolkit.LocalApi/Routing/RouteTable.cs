using SaveToolkit.LocalApi.Endpoints;
using SaveToolkit.LocalApi.Http;

namespace SaveToolkit.LocalApi.Routing;

/// <summary>An API operation: method, route template and handler, with optional pre-binding body limit.</summary>
internal sealed record LocalEndpoint(string Method, RouteTemplate Route, EndpointHandler Handler)
{
    /// <summary>Preset JSON envelopes are rejected above this size before binding.</summary>
    public int? PresetBodyLimit { get; init; }

    /// <summary>
    /// The endpoint accepts a JSON body (accepts metadata): a request with another Content-Type is
    /// rejected with 415 during routing, while a request without one reaches binding.
    /// </summary>
    public bool AcceptsJson { get; init; }
}

internal delegate ValueTask<LocalResult> EndpointHandler(EndpointContext context);

/// <summary>The outcome of route selection.</summary>
internal sealed record RouteSelection(LocalEndpoint? Endpoint, IReadOnlyList<string> AllowedMethods);

/// <summary>Selects an endpoint like the ASP.NET host's DFA matcher: path candidates, then method, then precedence.</summary>
internal sealed class RouteTable
{
    private readonly List<LocalEndpoint> _endpoints = [];

    public RouteTable Map(string method, string template, EndpointHandler handler,
        int? presetBodyLimit = null, bool json = false)
    {
        _endpoints.Add(new LocalEndpoint(method, new RouteTemplate(template), handler)
        {
            PresetBodyLimit = presetBodyLimit,
            AcceptsJson = json,
        });
        return this;
    }

    public IReadOnlyList<LocalEndpoint> Endpoints => _endpoints;

    /// <returns>The endpoint (with route values filled), or null with the allowed methods (405) or none (404).</returns>
    public RouteSelection Select(string method, string path, Dictionary<string, string> routeValues)
    {
        var segments = RouteTemplate.SplitPath(path);
        var candidates = new List<LocalEndpoint>();
        foreach (var endpoint in _endpoints)
        {
            if (endpoint.Route.TryMatch(segments, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
                candidates.Add(endpoint);
        }

        var allowed = candidates.Where(e => string.Equals(e.Method, method, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Route, Comparer<RouteTemplate>.Create(RouteTemplate.ComparePrecedence))
            .FirstOrDefault();
        if (allowed is not null)
        {
            allowed.Route.TryMatch(segments, routeValues);
            return new RouteSelection(allowed, []);
        }

        // The ASP.NET host maps a GET-only SPA fallback ({*path}) whose route constraint rejects /api
        // paths only after method selection. GET therefore never yields 405 (the fallback is chosen,
        // then rejected: 404), and every 405 lists GET among the allowed methods.
        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)) return new RouteSelection(null, []);
        var methods = candidates.Select(e => e.Method).Append("GET").Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).ToList();
        return new RouteSelection(null, methods);
    }
}
