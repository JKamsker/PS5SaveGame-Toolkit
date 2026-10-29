using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class OpenApiContractTests
{
    private const string DocumentRoute = "/openapi/v1.json";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Document_is_available_and_matches_the_contract(string environment)
    {
        using var host = CreateHost(environment);
        using var client = host.CreateClient();
        using var response = await client.GetAsync(DocumentRoute, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();
        Assert.StartsWith("3.", document["openapi"]!.GetValue<string>());
        OpenApiSnapshot.AssertMatches(document);

        var actual = document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject()
            .Select(method => method.Key.ToUpperInvariant() + " " + path.Key)).Order(StringComparer.Ordinal).ToArray();
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(method => method + " " + endpoint.RoutePattern.RawText!.TrimEnd('/')))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(actual);
        Assert.Equal(endpoints, actual);
    }

    internal static WebApplicationFactory<Program> CreateHost(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment(environment)
            .UseSetting("Toolkit:DataRoot", Path.Combine(Path.GetTempPath(), "openapi", Guid.NewGuid().ToString("N"), "workspaces"))
            .UseSetting("Toolkit:PackRoot", Path.Combine(Path.GetTempPath(), "openapi", Guid.NewGuid().ToString("N"), "packs"))
            .ConfigureLogging(logging => logging.ClearProviders()));
}
