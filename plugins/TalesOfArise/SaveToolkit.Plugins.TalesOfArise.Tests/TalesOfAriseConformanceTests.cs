using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class TalesOfAriseConformanceTests : PluginConformanceTests<TalesOfArisePlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA02745"];
    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields) =>
        [new ValueEdit("character/0/sp", FieldValue.Of(document.GetValue("character/0/sp").AsInt64() + 1))];
}
