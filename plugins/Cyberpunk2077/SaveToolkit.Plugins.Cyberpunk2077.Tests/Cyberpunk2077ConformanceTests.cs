using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class Cyberpunk2077ConformanceTests : PluginConformanceTests<Cyberpunk2077Plugin>
{
    [Fact]
    public void SyntheticFuzzSeedsOpenAndReachViews()
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
                    _ = queryable.Query(collection.Id, new() { IncludeEmpty = true, Limit = QueryableCollectionLimits.DefaultPageSize });
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA04027"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
    [
        MemorySaveSet.SingleFile("sav.dat", CsavCorruptionFixture.Flat(), "PPSA04027", "synthetic-stored"),
        MemorySaveSet.SingleFile("sav.dat", CsavCorruptionFixture.Flat(compressed: true), "PPSA04027", "synthetic-lz4"),
    ];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var money = Assert.Single(fields, field => field.Label == "Money (Items.money)" && !field.Spec.ReadOnly);
        var editable = fields.Where(field => !field.Spec.ReadOnly && (field.ValueId.StartsWith("progression/", StringComparison.Ordinal)
            || field.ValueId == "inventory/49/quantity" || field == money));
        return editable.Select(field => new ValueEdit(field.ValueId, FieldValue.Of(field.Value.AsInteger() + 1))).ToArray();
    }
}
