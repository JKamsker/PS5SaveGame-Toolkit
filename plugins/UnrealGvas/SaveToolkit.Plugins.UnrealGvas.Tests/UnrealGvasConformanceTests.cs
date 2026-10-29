using SaveToolkit.Testing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class UnrealGvasConformanceTests : PluginConformanceTests<UnrealGvasPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01288", "PPSA01603", "PPSA02199"];
    protected override int MaxFilesPerTitle => int.MaxValue;
    protected override IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds => [HogwartsDiscoveryFixture.Create()];

    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        if (!document.CanEdit) return [];
        var gameplay = fields.Where(f => f.Label.Contains("CharacterLevel", StringComparison.Ordinal)).ToArray();
        return base.ChooseEdits(document, gameplay.Length > 0 ? gameplay : fields.OrderBy(f => f.Spec switch
        {
            BoolSpec => 0, FloatSpec => 1, _ => 2,
        }).ToArray());
    }
}
