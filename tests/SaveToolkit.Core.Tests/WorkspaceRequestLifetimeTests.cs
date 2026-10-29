using Microsoft.AspNetCore.Mvc.Testing;
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
        using var probing = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var plugin = new BlockingProbePlugin(probing, resume);
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir());
            builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog([plugin])));
        });
        using var client = factory.CreateClient();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var store = factory.Services.GetRequiredService<WorkspaceStore>();
        var workspace = store.Create("request lifetime", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        var request = client.GetAsync(new Uri($"/api/workspaces/{workspace.Id}", UriKind.Relative), cancellation.Token);
        try
        {
            Assert.True(probing.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.True(store.Remove(workspace.Id));
            Assert.Null(store.Get(workspace.Id));
            Assert.True(Directory.Exists(workspace.Set.StorageRoot), "An active request still needs its imported blobs.");
            if (cancel) cancellation.Cancel();
            resume.Set();
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
            else
            {
                using var response = await request;
                response.EnsureSuccessStatusCode();
            }
            Assert.True(SpinWait.SpinUntil(() => !Directory.Exists(workspace.Set.StorageRoot), TimeSpan.FromSeconds(10)));
        }
        finally
        {
            resume.Set();
            try { using var response = await request; }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class BlockingProbePlugin(ManualResetEventSlim probing, ManualResetEventSlim resume) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.blocking-probe", "Blocking probe", "1.0.0");
        public ProbeResult Probe(ISaveFile file)
        {
            probing.Set();
            Assert.True(resume.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Equal(new byte[] { 1 }, file.ReadAllBytes());
            return ProbeResult.None;
        }
        public ISaveDocument Open(ISaveFile file) => throw new NotSupportedException();
    }
}
