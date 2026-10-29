using SaveToolkit.Sdk.Collections;
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

    [Fact]
    public void SyntheticFuzzSeedsOpenAndPreserveBytes()
    {
        var seeds = MutationFuzzSyntheticSeeds.ToArray();
        Assert.Single(seeds);
        foreach (var file in seeds)
        {
            Assert.True(Plugin.Probe(file).IsMatch);
            using var document = Plugin.Open(file);
            _ = document.BuildView();
            if (document is IQueryableDocument queryable)
                foreach (var collection in queryable.Collections)
                    _ = queryable.Query(collection.Id, new() { Limit = QueryableCollectionLimits.DefaultPageSize, IncludeEmpty = true });
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

}
