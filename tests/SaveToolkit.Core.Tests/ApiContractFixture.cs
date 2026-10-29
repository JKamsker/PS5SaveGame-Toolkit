using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class ApiContractFixture : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiContractFixture() : this(new CounterPlugin()) { }

    internal ApiContractFixture(params IGamePlugin[] plugins)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production").UseSetting("Toolkit:DataRoot", TestZips.TempDir())
                .UseSetting("Toolkit:PackRoot", TestZips.TempDir());
            builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog(plugins)));
        });
    }

    public HttpClient Client => field ??= _factory.CreateClient();
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<(string Workspace, string Document)> OpenAsync()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(CounterPlugin.Create(5)), "files", "保存.dat");
        form.Add(new StringContent("A dump / test"), "name");
        using var upload = await Client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, Ct);
        upload.EnsureSuccessStatusCode();
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var url = $"/api/workspaces/{workspace.GetProperty("id").GetString()}";
        var file = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0];
        using var opened = await Client.PostAsJsonAsync($"{url}/files/{file.GetProperty("id").GetString()}/open", new { }, Ct);
        opened.EnsureSuccessStatusCode();
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (url, $"{url}/documents/{document.GetProperty("id").GetString()}");
    }

    public async Task<HttpResponseMessage> PostAsync(string url, string json, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await Client.SendAsync(request, Ct);
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }
}
