using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Presentation only: native slots and their original field values are retained.</summary>
internal static class EmptySlots
{
    public const string Label = "Empty slot";
    public static EmptyRowPresentation Presentation { get; } = new("name");

    // Remake materia uses zero; inventories and Rebirth materia use the all-ones type marker.
    public static bool IsEmptyType(uint type, bool materia, Ff7Schema schema) =>
        type == (materia && schema.IsRemake ? 0U : uint.MaxValue);
}
