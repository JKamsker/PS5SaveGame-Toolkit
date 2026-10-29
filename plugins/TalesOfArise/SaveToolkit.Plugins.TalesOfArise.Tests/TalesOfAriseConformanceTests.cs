using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class TalesOfAriseConformanceTests : PluginConformanceTests<TalesOfArisePlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA02745"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [MemorySaveSet.SingleFile(AriseTestContainer.FileName, new AriseTestContainer().Encrypt(), AriseTestContainer.TitleId)];

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

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields) =>
        [new ValueEdit("character/0/sp", FieldValue.Of(document.GetValue("character/0/sp").AsInt64() + 1))];
}
