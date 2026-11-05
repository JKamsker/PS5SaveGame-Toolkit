using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.LocalApi;
using SaveToolkit.LocalApi.Tests.Parity;
using SaveToolkit.Sdk;

// Same name and surface as the framework tests' ASP.NET fixture, so their API contract test classes
// (linked into this project unchanged) run against the in-process LocalApiHost instead.
namespace SaveToolkit.Core.Tests;

public sealed class ApiContractFixture : IDisposable
{
    private readonly LocalApiHost _host;

    public ApiContractFixture() : this(new CounterPlugin()) { }

    internal ApiContractFixture(params IGamePlugin[] plugins)
    {
        _host = new LocalApiHost(new LocalApiOptions
        {
            DataRoot = ParityHosts.TempDir(),
            Catalog = new PluginCatalog(plugins),
            Version = ParityHosts.ApiVersion,
            SweepInterval = null,
        });
        Client = new HttpClient(new LocalApiHandler(_host)) { BaseAddress = new Uri("http://localhost") };
    }

    public HttpClient Client { get; }

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
        _host.Dispose();
    }
}
