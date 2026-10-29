using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class GodOfWarRagnarokConformanceTests : PluginConformanceTests<GodOfWarRagnarokPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA08330"];
    protected override int MaxFilesPerTitle => int.MaxValue;
    protected override int LazyExpansionBudget => 12;
    // Cover preferences and a real gameplay record, plus variable gear framing without samples in CI.
    protected override int MaxMutationFuzzFilesPerTitle => 3;
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        [RagnarokTestSupport.GameplayFile(RagnarokGearTests.Fixture())];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        var field = fields.FirstOrDefault(field => !field.Spec.ReadOnly && field.Value.AsInteger() > 0);
        return field is null ? [] : [new(field.ValueId, FieldValue.Of(field.Value.AsInteger() - 1))];
    }
}
