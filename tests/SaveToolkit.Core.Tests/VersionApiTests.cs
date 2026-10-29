using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class VersionApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Version_ReturnsTheBuiltApiVersionWithoutOpeningAWorkspace()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var version = body.GetProperty("version").GetString();
        Assert.Equal(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, version);
        Assert.Matches(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$", version);
    }
}
