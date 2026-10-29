using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceRequestLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingWorkspace_DuringRequest_RetainsBlobsUntilCompletion(bool cancel)
    {
        var probing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var plugin = new BlockingProbePlugin(probing, resume);
        using var factory = new IsolatedApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(new PluginCatalog([plugin]));
                services.AddSingleton<IStartupFilter>(new ResponseCompletionFilter(completed));
            });
        });
        using var client = factory.CreateClient();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var store = factory.Services.GetRequiredService<WorkspaceStore>();
        var workspace = store.Create("request lifetime", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        var request = client.GetAsync(new Uri($"/api/workspaces/{workspace.Id}", UriKind.Relative), cancellation.Token);
        try
        {
            await probing.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(store.Remove(workspace.Id));
            Assert.Null(store.Get(workspace.Id));
            Assert.True(Directory.Exists(workspace.Set.StorageRoot), "An active request still needs its imported blobs.");
            Assert.False(completed.Task.IsCompleted);
            if (cancel) cancellation.Cancel();
            resume.Set();
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
            else
            {
                using var response = await request;
                response.EnsureSuccessStatusCode();
            }
            await completed.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(Directory.Exists(workspace.Set.StorageRoot));
        }
        finally
        {
            resume.Set();
            try { using var response = await request; }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class ResponseCompletionFilter(TaskCompletionSource completed) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                // Completion callbacks run in reverse registration order. Register before the
                // endpoint's lease so this signal follows lease disposal, even after cancellation.
                context.Response.OnCompleted(() =>
                {
                    completed.TrySetResult();
                    return Task.CompletedTask;
                });
                await nextMiddleware();
            });
            next(app);
        };
    }

    private sealed class BlockingProbePlugin(TaskCompletionSource probing, ManualResetEventSlim resume) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.blocking-probe", "Blocking probe", "1.0.0");
        public ProbeResult Probe(ISaveFile file)
        {
            probing.TrySetResult();
            resume.Wait(TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 1 }, file.ReadAllBytes());
            return ProbeResult.None;
        }
        public ISaveDocument Open(ISaveFile file) => throw new NotSupportedException();
    }
}
