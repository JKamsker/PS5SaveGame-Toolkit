using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class FinalFantasy7ConformanceTests : PluginConformanceTests<FinalFantasy7Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01911", "PPSA08664", "PPSA18936"];

    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [
            MemorySaveSet.SingleFile("ff7remakecommon", ContainerTests.Common("PPSA01911"), "PPSA01911"),
            MemorySaveSet.SingleFile("ff7rebirthcommon", ContainerTests.Common("PPSA08664"), "PPSA08664"),
            MemorySaveSet.SingleFile("ff7rebirthdemocommon", ContainerTests.Common("PPSA18936"), "PPSA18936"),
        ];

    [Fact]
    public void MutationFuzzSyntheticSeedsOpenAndRender()
    {
        var seeds = MutationFuzzSyntheticSeeds.ToArray();
        Assert.Equal(3, seeds.Length);
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

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields) =>
        base.ChooseEdits(document, fields);
}
