using BenchmarkDotNet.Attributes;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;

namespace SaveToolkit.Benchmarks;

// BDN subclasses benchmark classes. Fresh-process first use is measured by --startup instead.
[MemoryDiagnoser]
public class CatalogStartupBenchmarks
{
    private PluginCatalog _catalog = null!;

    [GlobalSetup]
    public void Setup() => _catalog = PluginCatalog.Discover();

    [Benchmark]
    public PluginCatalog Discover() => PluginCatalog.Discover();

    // One full decoder invocation per iteration avoids calibration retaining many large packs.
    [IterationSetup(Target = nameof(LoadEmbeddedPacks))]
    public void PreparePackLoad() { }

    [Benchmark]
    public ReferencePackStore LoadEmbeddedPacks() => new(_catalog.Plugins);
}
