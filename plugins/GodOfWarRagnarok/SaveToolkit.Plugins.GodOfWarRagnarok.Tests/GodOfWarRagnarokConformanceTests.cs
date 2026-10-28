using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class GodOfWarRagnarokConformanceTests : PluginConformanceTests<GodOfWarRagnarokPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA08330"];
    protected override int MaxFilesPerTitle => int.MaxValue;
    protected override int LazyExpansionBudget => 12;
}
