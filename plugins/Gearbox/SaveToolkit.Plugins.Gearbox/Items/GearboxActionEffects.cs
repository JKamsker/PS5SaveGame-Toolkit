using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Display-only structural serial consequences; parsed counts, slots and presence keep their own metadata.</summary>
internal static class GearboxActionEffects
{
    private const int MaximumSerialTextLength = 6000;

    internal static ActionEffectDescriptor Serial(string list, string? role = null) =>
        new(ActionEffectKind.StructuralMemento, new TextSpec { ReadOnly = true, MaxLength = MaximumSerialTextLength })
        {
            Presentation = new FieldPresentation
            {
                Confidence = Confidence.Inferred,
                Description = "Exact serial payload; checksum and mapped layout do not establish game legality.",
                Warning = SerialWrites.Warning,
            },
            Path = role is null ? ["tab:Inventory", "table:" + list]
                : GearboxGroupIdentity.Policy.NavigationPath(role, ["tab:Inventory", "table:" + list]),
            Source = "Gearbox validated serial structural action",
        };
}
