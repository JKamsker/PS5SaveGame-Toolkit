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
        var field = fields.FirstOrDefault(f => !f.Spec.ReadOnly && f.Value.Kind == FieldValueKind.Union);
        if (field is null) return [];
        var current = field.Value.AsUnion();
        var edited = current.Type switch
        {
            "boolean" => FieldValue.Union("boolean", FieldValue.Of(!current.Value.AsBool())),
            "number" => JsonEditingTests.Number(current.Value.AsExactNumber() == "1" ? "2" : "1"),
            _ => JsonEditingTests.Text(current.Value.AsString() + " (test)"),
        };
        return [new ValueEdit(field.ValueId, edited)];
    }
}
