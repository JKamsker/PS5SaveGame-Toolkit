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
    public ReferencePackStore LoadEmbeddedPacks()
    {
        // Packs are validated on first need; LoadErrors validates all of them, as construction once did.
        var store = new ReferencePackStore(_catalog.Plugins);
        _ = store.LoadErrors;
        return store;
    }
}
