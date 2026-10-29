using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartConformanceTests : PluginConformanceTests<AtomicHeartPlugin>
{
    [Fact]
    public void SyntheticFuzzSeedsOpenAndReachViews()
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
                    _ = queryable.Query(collection.Id, new() { IncludeEmpty = true, Limit = QueryableCollectionLimits.DefaultPageSize });
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA10695"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [MemorySaveSet.SingleFile("slot", InventoryFixture.Create(), "PPSA10695")];
    protected override int LazyExpansionBudget => 20000;
    // The shared edit check only walks eager/lazy fields. Writable fields live in F8;
    // the query round trip below covers them without publishing a second inventory.
    protected override bool IsWritable => false;

    [Fact]
    public void QueryableEditSurvivesSerializeAndReopen()
    {
        foreach (var file in AllSelectedFiles())
        {
            using var document = Plugin.Open(file, WriteTestData.References());
            var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query(InventoryCollection.Id, new());
            var amount = page.Rows.Select(row => WriteTestData.Field(row, "amount")).First(field => !field.Spec.ReadOnly);
            var next = FieldValue.Of(amount.Value.AsInt64() - 1);
            Assert.True(document.ApplyEdits([new ValueEdit(amount.ValueId, next)]).Succeeded);
            using var reopened = WriteTestData.Open(WriteTestData.Output(document));
            Assert.Equal(next, reopened.GetValue(amount.ValueId));
        }
    }
}
