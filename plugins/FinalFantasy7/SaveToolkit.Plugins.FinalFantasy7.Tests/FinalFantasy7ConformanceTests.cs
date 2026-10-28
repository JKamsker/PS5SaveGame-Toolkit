using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class FinalFantasy7ConformanceTests : PluginConformanceTests<FinalFantasy7Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01911", "PPSA08664", "PPSA18936"];

    // There are deliberately no writable fields while PS5 integrity remains unknown.
    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields) => [];
}
