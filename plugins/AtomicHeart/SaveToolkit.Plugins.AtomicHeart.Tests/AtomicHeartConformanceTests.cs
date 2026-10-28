using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartConformanceTests : PluginConformanceTests<AtomicHeartPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA10695"];
    protected override int LazyExpansionBudget => 20000;
}
