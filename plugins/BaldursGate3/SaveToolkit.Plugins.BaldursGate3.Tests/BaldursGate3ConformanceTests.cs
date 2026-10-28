using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class BaldursGate3ConformanceTests : PluginConformanceTests<BaldursGate3Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA18463"];
}
