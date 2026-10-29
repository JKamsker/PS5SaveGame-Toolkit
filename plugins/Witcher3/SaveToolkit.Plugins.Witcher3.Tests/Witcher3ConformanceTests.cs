using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class Witcher3ConformanceTests : PluginConformanceTests<Witcher3Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA04018"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [MemorySaveSet.SingleFile("Synthetic.sav", SyntheticSave.Create(), "PPSA04018"),
         MemorySaveSet.SingleFile("Player.sav", SyntheticPlayerSave.Create(storedPoints: true), "PPSA04018")];

    [Fact]
    public void SyntheticFuzzSeedsOpenAndPreserveBytes()
    {
        var seeds = MutationFuzzSyntheticSeeds.ToArray();
        Assert.Equal(2, seeds.Length);
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

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var crowns = Assert.Single(fields, f => f.Label == "Crowns");
        return [new ValueEdit(crowns.ValueId, FieldValue.Of(crowns.Value.AsInteger() + 1))];
    }
}
