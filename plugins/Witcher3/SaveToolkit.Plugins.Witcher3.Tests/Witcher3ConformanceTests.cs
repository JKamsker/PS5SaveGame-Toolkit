using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class Witcher3ConformanceTests : PluginConformanceTests<Witcher3Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA04018"];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var crowns = Assert.Single(fields, f => f.Label == "Crowns");
        return [new ValueEdit(crowns.ValueId, FieldValue.Of(crowns.Value.AsInteger() + 1))];
    }
}
