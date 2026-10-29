using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SuckerPunchConformanceTests : PluginConformanceTests<SuckerPunchPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA02225", "PPSA26344"];

    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [new SyntheticSave(yotei: false).File(), new SyntheticSave(yotei: true).File()];

    [Fact]
    public void MutationFuzzSyntheticSeedsOpenAndRender()
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
                    _ = queryable.Query(collection.Id, new() { Limit = 1, IncludeEmpty = true });
        }
    }
    protected override int MaxFilesPerTitle => 32;

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var supplies = fields.FirstOrDefault(field => field.ValueId == "inventory/Supplies/quantity" && !field.Spec.ReadOnly);
        if (supplies is not null) return [new ValueEdit(supplies.ValueId,
            FieldValue.Of(supplies.Value.AsInteger() == 32767 ? 32766L : supplies.Value.AsInteger() + 1))];
        var quantity = fields.FirstOrDefault(field => field.ValueId.StartsWith("yotei/inventory/", StringComparison.Ordinal) &&
            field.ValueId.EndsWith("/quantity", StringComparison.Ordinal) && !field.Spec.ReadOnly);
        return quantity is null ? [] : [new ValueEdit(quantity.ValueId, FieldValue.Of(quantity.Value.AsInteger() == 0 ? 1L : quantity.Value.AsInteger() - 1))];
    }
}
