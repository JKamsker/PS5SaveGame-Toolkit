using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Holds a selected HTTP request before routing, allowing deterministic request ordering.</summary>
internal sealed class CleanCloseApiFixture : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public CleanCloseApiFixture(bool grouped, string? delayedMethod = null, string suffix = "")
    {
        Core = new CleanCloseFixture(grouped);
        DocumentPath = $"/api/workspaces/{Core.Workspace.Id}/documents/{Core.Session.Id}";
        Pause = delayedMethod is null ? null : new RequestPause(delayedMethod, DocumentPath + suffix);
        _factory = new IsolatedApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(Core.Store.Catalog);
                services.AddSingleton(Core.Store.Packs);
                services.AddSingleton(Core.Store);
                if (Pause is not null) services.AddSingleton<IStartupFilter>(Pause);
            });
        });
        Client = _factory.CreateClient();
    }

    public CleanCloseFixture Core { get; }
    public HttpClient Client { get; }
    public string DocumentPath { get; }
    public RequestPause? Pause { get; }
    public void Dispose()
    {
        Pause?.Resume.TrySetResult();
        Client.Dispose();
        _factory.Dispose();
        Core.Dispose();
    }

    internal sealed class RequestPause(string method, string path) : IStartupFilter
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Method == method && context.Request.Path == path)
                {
                    Entered.TrySetResult();
                    await Resume.Task.WaitAsync(context.RequestAborted);
                }
                await nextMiddleware();
            });
            next(app);
        };
    }
}
