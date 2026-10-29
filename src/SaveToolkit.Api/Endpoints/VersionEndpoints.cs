using System.Reflection;
using SaveToolkit.Api.Infrastructure;
using static Microsoft.AspNetCore.Http.StatusCodes;

namespace SaveToolkit.Api.Endpoints;

internal static class VersionEndpoints
{
    public static void MapVersionApi(this IEndpointRouteBuilder app)
    {
        var version = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        app.MapGet("/api/version", () => new { version }).AddEndpointFilter<ApiContractFilter>().WithApiErrors(Status500InternalServerError);
    }
}
