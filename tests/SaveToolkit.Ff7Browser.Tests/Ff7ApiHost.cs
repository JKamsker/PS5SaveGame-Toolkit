using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SaveToolkit.Testing;

namespace SaveToolkit.Ff7Browser.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Ff7ApiCollection : ICollectionFixture<Ff7ApiHost>
{
    public const string Name = "FF7 real import acceptance";
}

/// <summary>Real application endpoints and catalog, with process-owned writable storage.</summary>
public sealed class Ff7ApiHost : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private int _request;

    public Ff7ApiHost()
    {
        var evidence = Environment.GetEnvironmentVariable("SAVETOOLKIT_FF7_EVIDENCE") ?? TestScratch.Root;
        Root = Path.Combine(evidence, "ff7-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("Toolkit:DataRoot", Path.Combine(Root, "workspaces"))
            .UseSetting("Toolkit:PackRoot", Path.Combine(Root, "packs"))
            .UseSetting("Toolkit:PresetRoot", Path.Combine(Root, "presets")));
        Client = _factory.CreateClient();
    }

    public string Root { get; }
    public HttpClient Client { get; }
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task Retain(string suffix, byte[] bytes)
    {
        var path = Path.Combine(Root, $"{Interlocked.Increment(ref _request):D4}.{suffix}");
        await File.WriteAllBytesAsync(path, bytes, Ct);
    }

    public async Task<(int Status, JsonObject Body)> Send(string path, object? body = null, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await Client.SendAsync(request, Ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        var number = Interlocked.Increment(ref _request);
        var prefix = Path.Combine(Root, number.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        if (body is not null)
            await File.WriteAllBytesAsync(prefix + ".request.json", await request.Content!.ReadAsByteArrayAsync(Ct), Ct);
        await File.WriteAllBytesAsync(prefix + ".response.json", bytes, Ct);
        await File.WriteAllTextAsync(prefix + ".http.txt", $"{request.Method} {path}\n{(int)response.StatusCode}", Ct);
        return ((int)response.StatusCode, bytes.Length == 0 ? new JsonObject() : JsonNode.Parse(bytes)!.AsObject());
    }

    public async Task<JsonNode> Get(string path)
    {
        using var response = await Client.GetAsync(path, Ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        await File.WriteAllBytesAsync(Path.Combine(Root, $"{Interlocked.Increment(ref _request):D4}.response.json"), bytes, Ct);
        return JsonNode.Parse(bytes)!;
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        // Keep request/response evidence, remove only application-owned expanded bytes.
        foreach (var name in new[] { "workspaces", "packs", "presets" })
        {
            var directory = Path.Combine(Root, name);
            if (Path.GetDirectoryName(Path.GetFullPath(directory)) != Path.GetFullPath(Root))
                throw new InvalidOperationException("Application cleanup must remain inside its owned evidence root.");
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
