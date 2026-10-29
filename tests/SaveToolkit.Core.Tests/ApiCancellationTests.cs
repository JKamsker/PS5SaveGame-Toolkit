using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class ApiCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_is_quiet_and_never_a_server_error(bool aborted)
    {
        using var cancellation = new CancellationTokenSource();
        if (aborted) cancellation.Cancel();
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = cancellation.Token };
        using var body = new MemoryStream();
        context.Response.Body = body;
        var logs = new CapturedLogs<ToolkitExceptionHandler>();
        var handler = new ToolkitExceptionHandler(logs);

        Assert.True(await handler.TryHandleAsync(context, new OperationCanceledException("private diagnostic"), cancellation.Token));

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, context.Response.StatusCode);
        Assert.Empty(logs.Levels);
        if (aborted) Assert.Empty(body.ToArray());
        else
        {
            using var json = JsonDocument.Parse(body.ToArray());
            Assert.Equal(499, json.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(json.RootElement.GetProperty("error").GetString(), json.RootElement.GetProperty("detail").GetString());
            Assert.DoesNotContain("private diagnostic", json.RootElement.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Http_collection_query_observes_request_abort_and_releases_the_operation_gate()
    {
        var plugin = new QueryablePlugin();
        var logs = new CapturedLogs<Program>();
        using var factory = CreateFactory(plugin, logs);
        using var client = factory.CreateClient();
        var url = await OpenAsync(client);
        using var cancellation = new CancellationTokenSource();
        plugin.LastOpened!.TransformPage = page => { cancellation.Cancel(); return page; };

        var response = await factory.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = url + "/collections/items";
            context.RequestAborted = cancellation.Token;
        }, ApiContractFixture.Ct);

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, response.Response.StatusCode);
        Assert.DoesNotContain(logs.Levels, level => level >= LogLevel.Error);
        plugin.LastOpened.TransformPage = null;
        using var retry = await client.GetAsync(new Uri(url + "/collections/items", UriKind.Relative), ApiContractFixture.Ct);
        retry.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Http_cancellation_without_disconnected_client_retains_error_contract_without_error_logging()
    {
        var plugin = new QueryablePlugin();
        var logs = new CapturedLogs<Program>();
        using var factory = CreateFactory(plugin, logs);
        using var client = factory.CreateClient();
        var url = await OpenAsync(client);
        plugin.LastOpened!.TransformPage = _ => throw new OperationCanceledException("private diagnostic");

        using var response = await client.GetAsync(new Uri(url + "/collections/items", UriKind.Relative), ApiContractFixture.Ct);

        await ApiProblemContractTests.AssertProblemAsync(response, (System.Net.HttpStatusCode)499);
        Assert.DoesNotContain(logs.Levels, level => level >= LogLevel.Error);
    }

    private static WebApplicationFactory<Program> CreateFactory(QueryablePlugin plugin, ILoggerProvider logs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production").UseSetting("Toolkit:DataRoot", TestZips.TempDir())
            .UseSetting("Toolkit:PackRoot", TestZips.TempDir())
            .ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs))
            .ConfigureServices(services => services.AddSingleton(new PluginCatalog([plugin]))));

    private static async Task<string> OpenAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(QueryablePlugin.Create()), "files", "inventory.bin");
        using var upload = await client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, ApiContractFixture.Ct);
        upload.EnsureSuccessStatusCode();
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct);
        var url = $"/api/workspaces/{workspace.GetProperty("id").GetString()}";
        var file = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0];
        using var opened = await client.PostAsJsonAsync(url + "/files/" + file.GetProperty("id").GetString() + "/open", new { }, ApiContractFixture.Ct);
        opened.EnsureSuccessStatusCode();
        var document = await opened.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct);
        return url + "/documents/" + document.GetProperty("id").GetString();
    }

    private sealed class CapturedLogs<T> : ILogger<T>, ILoggerProvider
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Enqueue(logLevel);
        public void Dispose() { }
    }
}
