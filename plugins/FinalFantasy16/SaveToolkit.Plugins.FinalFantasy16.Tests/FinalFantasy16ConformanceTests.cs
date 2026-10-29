using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FinalFantasy16ConformanceTests : PluginConformanceTests<FinalFantasy16Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA10665", "PPSA13803"];

    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [FaithTestData.File(FaithCharacterizationData.Combined())];

    [Fact]
    public void MutationFuzzSyntheticSeedsOpenAndRender()
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
                    _ = queryable.Query(collection.Id, new() { Limit = 1, IncludeEmpty = true });
        }
    }
    protected override int MaxFilesPerTitle => 80;

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var gil = fields.FirstOrDefault(field => field.ValueId == "inventory/0/quantity" && !field.Spec.ReadOnly);
        return gil is null ? [] : [new ValueEdit(gil.ValueId, FieldValue.Of(gil.Value.AsInt64() + 1))];
    }
}
