using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class InsomniacConformanceTests : PluginConformanceTests<InsomniacPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01474"];
}
