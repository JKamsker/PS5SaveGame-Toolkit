using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FinalFantasy16ConformanceTests : PluginConformanceTests<FinalFantasy16Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA10665", "PPSA13803"];
    protected override int MaxFilesPerTitle => 80;

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var gil = fields.FirstOrDefault(field => field.ValueId == "inventory/0/quantity" && !field.Spec.ReadOnly);
        return gil is null ? [] : [new ValueEdit(gil.ValueId, FieldValue.Of(gil.Value.AsInt64() + 1))];
    }
}
