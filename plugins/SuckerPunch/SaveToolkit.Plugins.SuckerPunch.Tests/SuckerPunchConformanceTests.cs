using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SuckerPunchConformanceTests : PluginConformanceTests<SuckerPunchPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA02225", "PPSA26344"];
    protected override int MaxFilesPerTitle => 32;

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var supplies = fields.FirstOrDefault(field => field.ValueId == "inventory/Supplies/quantity" && !field.Spec.ReadOnly);
        if (supplies is not null) return [new ValueEdit(supplies.ValueId,
            FieldValue.Of(supplies.Value.AsInteger() == 32767 ? 32766L : supplies.Value.AsInteger() + 1))];
        var quantity = fields.FirstOrDefault(field => field.ValueId.StartsWith("yotei/inventory/", StringComparison.Ordinal) &&
            field.ValueId.EndsWith("/quantity", StringComparison.Ordinal) && !field.Spec.ReadOnly);
        return quantity is null ? [] : [new ValueEdit(quantity.ValueId, FieldValue.Of(quantity.Value.AsInteger() == 0 ? 1L : quantity.Value.AsInteger() - 1))];
    }
}
