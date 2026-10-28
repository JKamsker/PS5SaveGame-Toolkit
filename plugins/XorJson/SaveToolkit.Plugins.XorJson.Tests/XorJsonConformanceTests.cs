using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class XorJsonConformanceTests : PluginConformanceTests<XorJsonPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA16064"];
    protected override int MaxFilesPerTitle => int.MaxValue;

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        if (!document.CanEdit) return [];
        var volume = fields.SingleOrDefault(f => f.Label == "masterVolume");
        if (volume is not null) return [new ValueEdit(volume.ValueId, FieldValue.ExactNumber("49"))];
        var scalar = fields.First(f => !f.Spec.ReadOnly && (f.Value.Kind is FieldValueKind.Text or FieldValueKind.Bool or FieldValueKind.ExactNumber));
        return [new ValueEdit(scalar.ValueId, scalar.Value)];
    }
}
