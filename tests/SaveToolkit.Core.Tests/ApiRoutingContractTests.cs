using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class ApiRoutingContractTests
{
    [Fact]
    public async Task Built_spa_stays_available_without_masking_api_errors_or_static_head()
    {
        string webRoot = TestZips.TempDir();
        Directory.CreateDirectory(webRoot);
        const string page = "<!doctype html><title>SPA navigation fixture</title>";
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), page, ApiContractFixture.Ct);
        // Production disables the Development static-web-assets manifest, which would otherwise layer a
        // locally built src/SaveToolkit.Api/wwwroot over this fixture web root.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseWebRoot(webRoot).UseSetting("Toolkit:DataRoot", TestZips.TempDir())
            .UseSetting("Toolkit:PackRoot", TestZips.TempDir()));
        using var client = factory.CreateClient();

        using var navigation = await client.GetAsync(new Uri("/editor/deep-link", UriKind.Relative), ApiContractFixture.Ct);
        Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);
        Assert.Equal("text/html", navigation.Content.Headers.ContentType?.MediaType);
        Assert.Equal(page, await navigation.Content.ReadAsStringAsync(ApiContractFixture.Ct));

        string[] apiPaths = ["/api", "/api/no-such-endpoint", "/API/no-such-endpoint", "/api/missing.json"];
        foreach (string path in apiPaths)
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), ApiContractFixture.Ct);
            await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.NotFound);
        }
        using var head = new HttpRequestMessage(HttpMethod.Head, new Uri("/index.html", UriKind.Relative));
        using var staticFile = await client.SendAsync(head, ApiContractFixture.Ct);
        Assert.Equal(HttpStatusCode.OK, staticFile.StatusCode);
        Assert.Equal("text/html", staticFile.Content.Headers.ContentType?.MediaType);
    }
}
