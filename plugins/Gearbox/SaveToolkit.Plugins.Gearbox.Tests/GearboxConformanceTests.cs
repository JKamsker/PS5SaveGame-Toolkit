using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GearboxConformanceTests : PluginConformanceTests<GearboxPlugin>
{
    [Fact]
    public void SyntheticFuzzSeedsOpenAndReachViews()
    {
        var seeds = MutationFuzzSyntheticSeeds.ToArray();
        Assert.Equal(4, seeds.Length);
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

    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01463", "PPSA01493"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
    [
        GearboxTestData.File("PPSA01463", GearboxTestData.CreateContainer(false, false)),
        GearboxTestData.File("PPSA01463", GearboxTestData.CreateContainer(false, true), "profile"),
        GearboxTestData.File("PPSA01493", GearboxTestData.CreateContainer(true, false)),
        GearboxTestData.File("PPSA01493", GearboxTestData.CreateContainer(true, true), "profile"),
    ];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var currency = fields.FirstOrDefault(f => f.ValueId == "currency/618814354" && !f.Spec.ReadOnly)
            ?? fields.FirstOrDefault(f => f.ValueId.StartsWith("keys/", StringComparison.Ordinal) && !f.Spec.ReadOnly);
        return currency is null ? [] : [new ValueEdit(currency.ValueId, FieldValue.Of(currency.Value.AsInt64() + 1))];
    }
}
