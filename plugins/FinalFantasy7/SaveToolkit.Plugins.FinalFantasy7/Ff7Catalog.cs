namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Legacy type-name fallback and source character order; names never imply write eligibility.</summary>
internal static class Ff7Catalog
{
    public static string Item(Ff7Schema schema, uint id) => ItemNames.Lookup(schema, false, id);

    public static string Materia(Ff7Schema schema, uint id) => ItemNames.Lookup(schema, true, id);

    public static string Character(Ff7Schema schema, int index) => index switch
    {
        0 => "Cloud Strife",
        1 => "Barret Wallace",
        2 => "Tifa Lockhart",
        3 => "Aerith Gainsborough",
        4 => "Red XIII",
        5 => "Yuffie Kisaragi",
        6 => schema.IsRemake ? "Sonon Kusakabe" : "Cait Sith",
        _ => $"Character {index}",
    };
}
