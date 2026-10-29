using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class GodOfWarRagnarokConformanceTests : PluginConformanceTests<GodOfWarRagnarokPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA08330"];

    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [
            RagnarokTestSupport.GameplayFile(new RagnarokNestedFixture().Create()),
            RagnarokTestSupport.GameplayFile(RagnarokGearTests.Fixture()),
        ];

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
    protected override int MaxFilesPerTitle => int.MaxValue;
    protected override int LazyExpansionBudget => 12;
    // Cover preferences and a real gameplay record, plus variable gear framing without samples in CI.
    protected override int MaxMutationFuzzFilesPerTitle => 3;

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var field = fields.FirstOrDefault(field => !field.Spec.ReadOnly && field.Value.AsInteger() > 0);
        return field is null ? [] : [new(field.ValueId, FieldValue.Of(field.Value.AsInteger() - 1))];
    }
}
