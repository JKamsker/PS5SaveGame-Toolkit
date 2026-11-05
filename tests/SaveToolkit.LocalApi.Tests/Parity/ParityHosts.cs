using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.LocalApi.Tests.Parity;

/// <summary>The ASP.NET host and the in-process host, each with isolated storage and its own plugin instances.</summary>
internal sealed class ParityHosts : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly LocalApiHost _local;

    public ParityHosts(Func<IGamePlugin[]>? plugins = null)
    {
        plugins ??= DefaultPlugins;
        var aspNetPlugins = plugins();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production").UseSetting("Toolkit:DataRoot", TempDir())
                .UseSetting("Toolkit:PackRoot", TempDir())
                .UseSetting("Toolkit:PresetRoot", TempDir());
            builder.ConfigureServices(services => services.AddSingleton(new PluginCatalog(aspNetPlugins)));
        });
        _local = new LocalApiHost(new LocalApiOptions
        {
            DataRoot = TempDir(),
            Catalog = new PluginCatalog(plugins()),
            Version = ApiVersion,
            SweepInterval = null,
        });
        AspNet = _factory.CreateClient();
        Local = new HttpClient(new LocalApiHandler(_local)) { BaseAddress = new Uri("http://localhost") };
    }

    public static string ApiVersion { get; } = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    public HttpClient AspNet { get; }

    public HttpClient Local { get; }

    public LocalApiHost LocalHost => _local;

    public static IGamePlugin[] DefaultPlugins() =>
        [new CounterPlugin(), new ItemsPlugin(), new QueryablePlugin(), new PlanningPlugin()];

    public static string TempDir() => Path.Combine(TestScratch.ProcessFolder, "localapi-" + Guid.NewGuid().ToString("N"));

    /// <summary>Runs one scenario against both hosts and asserts identical normalized transcripts.</summary>
    public async Task AssertParityAsync(Func<Transcript, Task> scenario)
    {
        var expected = new Transcript(AspNet);
        await scenario(expected);
        var actual = new Transcript(Local);
        await scenario(actual);
        if (Environment.GetEnvironmentVariable("SAVETOOLKIT_PARITY_DUMP") is { Length: > 0 } dump)
        {
            Directory.CreateDirectory(dump);
            var name = TestContext.Current.Test?.TestDisplayName ?? "scenario";
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            await File.WriteAllLinesAsync(Path.Combine(dump, name + ".aspnet.txt"), expected.Entries, TestContext.Current.CancellationToken);
            await File.WriteAllLinesAsync(Path.Combine(dump, name + ".local.txt"), actual.Entries, TestContext.Current.CancellationToken);
        }

        Assert.Equal(expected.Entries.Count, actual.Entries.Count);
        for (var i = 0; i < expected.Entries.Count; i++) Assert.Equal(expected.Entries[i], actual.Entries[i]);
    }

    public void Dispose()
    {
        AspNet.Dispose();
        Local.Dispose();
        _local.Dispose();
        _factory.Dispose();
    }
}
