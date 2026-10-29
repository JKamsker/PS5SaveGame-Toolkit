using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class EldenRingConformanceTests : PluginConformanceTests<EldenRingPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA03169"];

    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [new SyntheticSave([0]).File()];

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

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var runes = fields.First(field => field.ValueId == "slots/0/runes");
        return [new ValueEdit(runes.ValueId, FieldValue.Of(runes.Value.AsInteger() + 1))];
    }
}
