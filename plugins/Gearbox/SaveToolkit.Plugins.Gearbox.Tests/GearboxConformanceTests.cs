using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GearboxConformanceTests : PluginConformanceTests<GearboxPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01463", "PPSA01493"];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var currency = fields.FirstOrDefault(f => f.ValueId == "currency/618814354" && !f.Spec.ReadOnly)
            ?? fields.FirstOrDefault(f => f.ValueId.StartsWith("keys/", StringComparison.Ordinal) && !f.Spec.ReadOnly);
        return currency is null ? [] : [new ValueEdit(currency.ValueId, FieldValue.Of(currency.Value.AsInt64() + 1))];
    }
}
