using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class UnrealGvasConformanceTests : PluginConformanceTests<UnrealGvasPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01288", "PPSA01603", "PPSA02199"];
    protected override int MaxFilesPerTitle => int.MaxValue;
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds => [HogwartsDiscoveryFixture.Create()];

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
        if (!document.CanEdit) return [];
        var gameplay = fields.Where(f => f.Label.Contains("CharacterLevel", StringComparison.Ordinal)).ToArray();
        return base.ChooseEdits(document, gameplay.Length > 0 ? gameplay : fields.OrderBy(f => f.Spec switch
        {
            BoolSpec => 0, FloatSpec => 1, _ => 2,
        }).ToArray());
    }
}
