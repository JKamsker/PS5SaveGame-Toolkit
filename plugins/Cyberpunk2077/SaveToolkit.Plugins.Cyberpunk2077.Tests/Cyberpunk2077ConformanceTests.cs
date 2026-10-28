using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class Cyberpunk2077ConformanceTests : PluginConformanceTests<Cyberpunk2077Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA04027"];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var money = Assert.Single(fields, field => field.Label == "Money (Items.money)" && !field.Spec.ReadOnly);
        return [new ValueEdit(money.ValueId, FieldValue.Of(money.Value.AsInteger() + 1))];
    }
}
