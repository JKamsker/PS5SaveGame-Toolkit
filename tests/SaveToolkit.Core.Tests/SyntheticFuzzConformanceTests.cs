using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

/// <summary>Runs inherited fuzzing in sample-free CI; unrelated sample-only facts skip normally.</summary>
public sealed class SyntheticFuzzConformanceTests : PluginConformanceTests<CounterPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA99999"];
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [MemorySaveSet.SingleFile("counter.dat", CounterPlugin.Create(5), "PPSA99999")];
}
