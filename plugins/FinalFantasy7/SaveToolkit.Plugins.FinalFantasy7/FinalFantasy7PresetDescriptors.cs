using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal static class FinalFantasy7PresetDescriptors
{
    private const string CapabilitySchema = "ff7r.presets.v1";
    private const string PresetKind = "ff7r.action-parameters";

    public static PresetCapability Create(Ff7Schema schema, bool readOnly, string? referenceIdentity) =>
        new(CapabilitySchema, null, readOnly ? [] : Actions(schema), 1, Variant(schema.Variant), referenceIdentity,
            PresetKind, $"ff7r.save.v1/{Variant(schema.Variant)}/{(readOnly ? "common" : "gameplay")}/{schema.FileLength(readOnly)}",
            readOnly ? "common" : "gameplay");

    private static IReadOnlyList<PresetActionCapability> Actions(Ff7Schema schema) =>
    [
        new(InventoryPlanning.ActionId, "Restock existing consumables", ["quantity"], ParameterSpecs: [IntegerSpec.UInt32 with { Min = 1, Max = 99 }], ScopeKinds: [PresetScopeKinds.Document, PresetScopeKinds.AllEligible, PresetScopeKinds.Selected]),
        new(MateriaPlanning.ActionId, "Set materia AP within tier", ["type", "ap"], ParameterSpecs: [new ReferenceSpec("ff7r-materia"), IntegerSpec.UInt32 with { Max = MateriaEditPolicy.MaximumTargetAp }], ScopeKinds: schema.IsRemake ? [PresetScopeKinds.Document, PresetScopeKinds.AllCopies] : [PresetScopeKinds.Document, PresetScopeKinds.AllCopies, PresetScopeKinds.Selected]),
        new(MateriaPlanning.StarActionId, "Set materia stars", ["type", "stars"], ParameterSpecs: [new ReferenceSpec("ff7r-materia"), IntegerSpec.UInt8 with { Min = 1, Max = 5 }], ScopeKinds: schema.IsRemake ? [PresetScopeKinds.Document, PresetScopeKinds.AllCopies] : [PresetScopeKinds.Document, PresetScopeKinds.AllCopies, PresetScopeKinds.Selected]),
        new(CharacterPlanning.RecoveryActionId, "Recover current character vitals", [], ParameterSpecs: [], ScopeKinds: [PresetScopeKinds.Document, PresetScopeKinds.AllEligible, PresetScopeKinds.Selected]),
        new(CharacterPlanning.LevelActionId, "Set character level", ["level"], ParameterSpecs: [IntegerSpec.UInt8 with { Min = 1, Max = schema.IsRemake ? 50 : 70 }], ScopeKinds: [PresetScopeKinds.Document, PresetScopeKinds.AllEligible, PresetScopeKinds.Selected]),
    ];

    private static string Variant(Ff7Variant variant) => variant switch
    {
        Ff7Variant.Remake => "remake",
        Ff7Variant.Rebirth => "rebirth",
        Ff7Variant.RebirthDemo => "rebirth-demo",
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };
}
