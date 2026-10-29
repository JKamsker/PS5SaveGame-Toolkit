using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class PragmataConformanceTests : PluginConformanceTests<PragmataPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA02530"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [MemorySaveSet.SingleFile("data000.bin", SyntheticInventory.Create(2), "PPSA02530")];
    protected override int MaxFilesPerTitle => int.MaxValue;

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

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var quantity = fields.FirstOrDefault(f => f.Label == "Quantity");
        if (quantity is not null)
            return [new ValueEdit(quantity.ValueId, FieldValue.Of(quantity.Value.AsInteger() + 1))];
        var setting = fields.First(f => f.Spec is BoolSpec && !f.Spec.ReadOnly);
        return [new ValueEdit(setting.ValueId, FieldValue.Of(!setting.Value.AsBool()))];
    }
}
