using SaveToolkit.Testing;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class InsomniacConformanceTests : PluginConformanceTests<InsomniacPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01474"];
    protected override int MaxFilesPerTitle => 128;
    // Cover both preference copies and a gameplay seed instead of only the smallest preference file.
    protected override int MaxMutationFuzzFilesPerTitle => 3;
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [MemorySaveSet.SingleFile("i29-s.save", CurrencyDiscoveryTests.Fixture(
            (RiftCurrencyView.BoltConfig, 123456u), (RiftCurrencyView.RaritaniumConfig, 37u)), "PPSA01474")];
}
