using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SuckerPunchConformanceTests : PluginConformanceTests<SuckerPunchPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA02225", "PPSA26344"];
}
