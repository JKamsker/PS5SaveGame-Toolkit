using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class BaldursGate3ConformanceTests : PluginConformanceTests<BaldursGate3Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA18463"];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        // Exercise a meaningful save-metadata string with a length change, not just an arbitrary flag.
        var leader = fields.FirstOrDefault(f => !f.Spec.ReadOnly && f.Label.StartsWith("LeaderName (", StringComparison.Ordinal));
        return leader is null ? base.ChooseEdits(document, fields) :
            [new ValueEdit(leader.ValueId, FieldValue.Of(leader.Value.AsString() + " test"))];
    }
}
