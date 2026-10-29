using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroPlayroomConformanceTests : PluginConformanceTests<AstroPlayroomPlugin>
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

    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01325"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [AstroTestData.File(AstroTestData.Bytes())];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var coins = Assert.Single(fields, field => field.Description == "PreInstall/Common/NumCoin");
        return [new ValueEdit(coins.ValueId, FieldValue.Of(coins.Value.AsInteger() + 10000))];
    }
}
