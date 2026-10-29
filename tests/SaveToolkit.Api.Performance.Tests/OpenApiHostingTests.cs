using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class OpenApiHostingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Contract_preserves_static_spa_and_unknown_api_responses()
    {
        string root = Path.Combine(Path.GetTempPath(), "openapi-hosting", Guid.NewGuid().ToString("N"));
        string webRoot = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(webRoot);
        const string page = "<!doctype html><title>OpenAPI hosting regression fixture</title>";
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), page, Ct);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "fixture.js"), "export const fixture = true;", Ct);
        using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production").UseWebRoot(webRoot)
            .UseSetting("Toolkit:DataRoot", Path.Combine(root, "workspaces"))
            .UseSetting("Toolkit:PackRoot", Path.Combine(root, "packs")));
        using var client = host.CreateClient();
        using var contract = await client.GetAsync("/openapi/v1.json", Ct);
        Assert.Equal(HttpStatusCode.OK, contract.StatusCode);
        Assert.Equal("application/json", contract.Content.Headers.ContentType?.MediaType);
        Assert.Equal(page, await client.GetStringAsync("/editor/deep-link", Ct));
        using var head = new HttpRequestMessage(HttpMethod.Head, "/fixture.js");
        using var asset = await client.SendAsync(head, Ct);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.True(asset.Content.Headers.ContentLength > 0);
        foreach (string path in new[] { "/api", "/api/unknown", "/API/unknown", "/api/unknown.json" })
        {
            using var response = await client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var problem = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(Ct));
            Assert.Equal(404, problem.GetProperty("status").GetInt32());
            Assert.Equal("about:blank", problem.GetProperty("type").GetString());
            Assert.Equal(problem.GetProperty("detail").GetString(), problem.GetProperty("error").GetString());
        }
    }
}
