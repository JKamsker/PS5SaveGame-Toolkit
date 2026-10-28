using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class EldenRingConformanceTests : PluginConformanceTests<EldenRingPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA03169"];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var runes = fields.First(field => field.ValueId == "slots/0/runes");
        return [new ValueEdit(runes.ValueId, FieldValue.Of(runes.Value.AsInteger() + 1))];
    }
}
