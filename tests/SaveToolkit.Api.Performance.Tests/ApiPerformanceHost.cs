using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Api.Performance.Tests;

internal sealed class ApiPerformanceHost : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiPerformanceHost(IGamePlugin? plugin = null)
    {
        var root = Path.Combine(TestScratch.ProcessFolder, "api-performance", Guid.NewGuid().ToString("N"));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("Toolkit:DataRoot", Path.Combine(root, "workspaces"))
            .UseSetting("Toolkit:PackRoot", Path.Combine(root, "packs"))
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                if (plugin is not null) services.AddSingleton(new PluginCatalog([plugin]));
            }));
        Client = _factory.CreateClient();
        Client.Timeout = TimeSpan.FromMinutes(10);
    }

    public HttpClient Client { get; }
    internal string StorageRoot => _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["Toolkit:DataRoot"]!;
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<JsonElement> ImportAsync(Stream source, string name)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(source), "files", name);
        using var response = await Client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, Ct);
        return await ReadAsync(response);
    }

    public async Task<JsonElement> PostAsync(string url, object body)
    {
        using var response = await Client.PostAsJsonAsync(url, body, Ct);
        return await ReadAsync(response);
    }

    public async Task<JsonElement> GetAsync(string url)
    {
        using var response = await Client.GetAsync(url, Ct);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }
}
