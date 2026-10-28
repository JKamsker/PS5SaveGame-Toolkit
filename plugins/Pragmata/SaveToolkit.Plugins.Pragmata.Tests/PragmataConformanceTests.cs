using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class PragmataConformanceTests : PluginConformanceTests<PragmataPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA02530"];
    protected override int MaxFilesPerTitle => int.MaxValue;

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var quantity = fields.FirstOrDefault(f => f.Label == "Quantity");
        if (quantity is not null)
            return [new ValueEdit(quantity.ValueId, FieldValue.Of(quantity.Value.AsInteger() + 1))];
        var setting = fields.First(f => f.Spec is BoolSpec && !f.Spec.ReadOnly);
        return [new ValueEdit(setting.ValueId, FieldValue.Of(!setting.Value.AsBool()))];
    }
}
