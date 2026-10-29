using System.Reflection;

namespace SaveToolkit.Api.Endpoints;

internal static class VersionEndpoints
{
    public static void MapVersionApi(this IEndpointRouteBuilder app)
    {
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        app.MapGet("/api/version", () => new { version });
    }
}
