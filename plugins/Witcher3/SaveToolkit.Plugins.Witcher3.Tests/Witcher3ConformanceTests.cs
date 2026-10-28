using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class Witcher3ConformanceTests : PluginConformanceTests<Witcher3Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA04018"];
}
