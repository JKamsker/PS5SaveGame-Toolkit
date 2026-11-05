using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceOpenAbortTests
{
    [Fact]
    public async Task AbortedOpen_DoesNotPublishDocument()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var plugin = new BlockingPlugin(entered, resume);
        using var factory = new IsolatedApiFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(new PluginCatalog([plugin]));
            services.AddSingleton<IStartupFilter>(new AbortFilter(aborted, finished));
        }));
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<WorkspaceStore>();
        var ws = store.Create("abort", [new ImportSource("counter.dat", () => new MemoryStream(CounterPlugin.Create(1)))]);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var request = client.PostAsJsonAsync($"/api/workspaces/{ws.Id}/files/{ws.Set.AllDiskFiles.Single().Id}/open",
            new { pluginId = plugin.Info.Id }, cancel.Token);
        try
        {
            await TestWorker.WaitForSignalAsync(entered.Task, request, timeout.Token);
            cancel.Cancel();
            await aborted.Task.WaitAsync(timeout.Token);
        }
        finally { resume.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        await finished.Task.WaitAsync(timeout.Token);
        Assert.Empty(ws.Documents);
    }

    private sealed class BlockingPlugin(TaskCompletionSource entered, ManualResetEventSlim resume) : IGamePlugin
    {
        private readonly CounterPlugin _inner = new();
        public PluginInfo Info => _inner.Info;
        public ProbeResult Probe(ISaveFile file) => _inner.Probe(file);
        public ISaveDocument Open(ISaveFile file)
        {
            entered.TrySetResult();
            if (!resume.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            return _inner.Open(file);
        }
    }

    private sealed class AbortFilter(TaskCompletionSource aborted, TaskCompletionSource finished) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                using var registration = context.RequestAborted.Register(() => aborted.TrySetResult());
                try { await following(); }
                finally { finished.TrySetResult(); }
            });
            next(app);
        };
    }
}
