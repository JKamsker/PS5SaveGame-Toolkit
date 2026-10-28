using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class GenericJsonConformanceTests : PluginConformanceTests<GenericJsonPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA04027", "PPSA07696", "PPSA02343"];

    protected override int MaxFilesPerTitle => 100;

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        // Change an actual option or metadata value, including text-only JSON files.
        var field = fields.FirstOrDefault(f => !f.Spec.ReadOnly && f.Spec is BoolSpec)
            ?? fields.FirstOrDefault(f => !f.Spec.ReadOnly);
        return field is null ? [] : [new ValueEdit(field.ValueId, field.Spec switch
        {
            BoolSpec => FieldValue.Of(!field.Value.AsBool()),
            IntegerSpec => FieldValue.Of(field.Value.AsInteger() + 1),
            FloatSpec => FieldValue.Of(field.Value.AsDouble() + 0.25),
            _ => FieldValue.Of(field.Value.AsString() + " (test)"),
        })];
    }
}
