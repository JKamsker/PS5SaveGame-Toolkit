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
        return supplies is null ? [] : [new ValueEdit(supplies.ValueId,
            FieldValue.Of(supplies.Value.AsInteger() == 32767 ? 32766L : supplies.Value.AsInteger() + 1))];
    }
}
