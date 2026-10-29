using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class BaldursGate3ConformanceTests : PluginConformanceTests<BaldursGate3Plugin>
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

    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA18463"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
    [
        MemorySaveSet.SingleFile("synthetic.lsf", ResourceFixture.Create(), "PPSA18463"),
        MemorySaveSet.SingleFile("synthetic.lsv", PackageFixture.Create(method: 3,
            payloads: [("Globals.lsf", ResourceFixture.Create(adjacency: true, compression: 2))]), "PPSA18463"),
    ];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        // Exercise a meaningful save-metadata string with a length change, not just an arbitrary flag.
        var leader = fields.FirstOrDefault(f => !f.Spec.ReadOnly && f.Label.StartsWith("LeaderName (", StringComparison.Ordinal));
        return leader is null ? base.ChooseEdits(document, fields) :
            [new ValueEdit(leader.ValueId, FieldValue.Of(leader.Value.AsString() + " test"))];
    }
}
