using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SaveToolkit.Core.Tests;

public sealed class ApiDocumentationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Reference_matches_every_real_api_method_and_route_in_both_directions()
    {
        using var host = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Toolkit:DataRoot", TestZips.TempDir()).UseSetting("Toolkit:PackRoot", TestZips.TempDir()));
        using var client = host.CreateClient();
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is "/api"
                || endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(method => method + " " + endpoint.RoutePattern.RawText!.TrimEnd('/')))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(endpoints);
        Assert.Contains("POST /api/workspaces/{workspaceId}/documents/{documentId}/reference-fields/records", endpoints);
        Assert.Equal(endpoints.Length, endpoints.Distinct(StringComparer.Ordinal).Count());

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SaveToolkit.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string reference = Path.Combine(directory.FullName, "docs", "API.md");
        Assert.True(File.Exists(reference), "Missing docs/API.md.");
        string markdown = File.ReadAllText(reference);
        var documented = Regex.Matches(markdown, @"(?m)^### `(GET|POST|DELETE|PUT|PATCH|HEAD|OPTIONS) (/api(?:/[^`]+)?)`\r?$")
            .Select(match => match.Groups[1].Value + " " + match.Groups[2].Value.TrimEnd('/'))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(documented.Length, documented.Distinct(StringComparer.Ordinal).Count());
        Assert.True(endpoints.SequenceEqual(documented, StringComparer.Ordinal),
            "Missing from docs: " + string.Join(", ", endpoints.Except(documented, StringComparer.Ordinal))
            + "\nDocumented but absent: " + string.Join(", ", documented.Except(endpoints, StringComparer.Ordinal)));
    }
}
