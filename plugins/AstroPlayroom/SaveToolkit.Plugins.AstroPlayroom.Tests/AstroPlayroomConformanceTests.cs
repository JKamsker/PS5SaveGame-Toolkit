using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroPlayroomConformanceTests : PluginConformanceTests<AstroPlayroomPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01325"];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var coins = Assert.Single(fields, field => field.Description == "PreInstall/Common/NumCoin");
        return [new ValueEdit(coins.ValueId, FieldValue.Of(coins.Value.AsInteger() + 10000))];
    }
}
