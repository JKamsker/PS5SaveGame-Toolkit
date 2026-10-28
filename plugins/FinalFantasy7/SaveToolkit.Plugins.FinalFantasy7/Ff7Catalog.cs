using System.Collections.Frozen;
using System.Text.Json;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Small embedded numerical catalog; names are MIT-derived and never imply write eligibility.</summary>
internal static class Ff7Catalog
{
    private static readonly FrozenDictionary<uint, string> Weapons = LoadWeapons();

    public static string Item(Ff7Schema schema, uint id) => ItemNames.Lookup(schema, false, id);

    public static string Materia(Ff7Schema schema, uint id) => ItemNames.Lookup(schema, true, id);

    public static string Weapon(uint id) => Weapons.TryGetValue(id, out var name) ? name : $"Unknown weapon 0x{id:X}";

    public static string Character(Ff7Schema schema, int index) => schema.IsRemake
        ? index switch
        {
            0 => "Cloud Strife",
            1 => "Barret Wallace",
            2 => "Tifa Lockhart",
            3 => "Aerith Gainsborough",
            4 => "Red XIII",
            5 => "Yuffie Kisaragi",
            6 => "Sonon Kusakabe",
            _ => $"Character {index}",
        }
        : index switch
        {
            0 => "Cloud Strife",
            1 => "Barret Wallace",
            2 => "Tifa Lockhart",
            3 => "Aerith Gainsborough",
            4 => "Red XIII",
            5 => "Yuffie Kisaragi",
            6 => "Cait Sith",
            _ => $"Character {index}",
        };

    private static FrozenDictionary<uint, string> LoadWeapons()
    {
        using var stream = typeof(Ff7Catalog).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.FinalFantasy7.Resources.equipment.json")
            ?? throw new InvalidOperationException("Embedded FFVII equipment data is missing.");
        var values = JsonSerializer.Deserialize<Dictionary<uint, string>>(stream)
            ?? throw new InvalidOperationException("Embedded FFVII equipment data is invalid.");
        return values.ToFrozenDictionary();
    }
}
