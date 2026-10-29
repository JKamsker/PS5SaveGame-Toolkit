namespace SaveToolkit.Api.Infrastructure;

/// <summary>Keeps the SPA fallback out of API routing, including automatic 404/405 selection.</summary>
internal sealed class SpaPathConstraint : IRouteConstraint
{
    internal const string Name = "spaPath";
    internal const string Pattern = "{*path:nonfile:" + Name + "}";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey,
        RouteValueDictionary values, RouteDirection routeDirection)
    {
        string path = values[routeKey] as string ?? string.Empty;
        return !path.Equals("api", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("api/", StringComparison.OrdinalIgnoreCase);
    }
}
