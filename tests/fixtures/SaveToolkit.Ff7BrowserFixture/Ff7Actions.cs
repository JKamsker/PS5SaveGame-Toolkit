namespace SaveToolkit.Testing.Ff7Browser;

/// <summary>Published F6 action identifiers; all binary addresses come from the plugin's test seam.</summary>
public static class Ff7Actions
{
    public const string Restock = "inventory/restock-existing";
    public const string Ap = "materia/set-ap-within-tier";
    public const string Stars = "materia/set-stars";
    public const string Recover = "character/recover-current-vitals";
    public const string Level = "character/set-level";
    public static IReadOnlyList<string> All { get; } = [Restock, Ap, Stars, Recover, Level];
    public static IReadOnlyList<string> Titles { get; } = ["PPSA01911", "PPSA08664", "PPSA18936"];

    public static object Parameters(string action, Ff7Binary fixture) => action switch
    {
        Restock => new { quantity = "99" },
        Ap => new { type = fixture.MateriaType.ToString(System.Globalization.CultureInfo.InvariantCulture), ap = "1" },
        Stars => new { type = fixture.MateriaType.ToString(System.Globalization.CultureInfo.InvariantCulture), stars = "2" },
        Recover => new { },
        Level => new { level = "25" },
        _ => throw new ArgumentException("Unknown authored acceptance action.", nameof(action)),
    };

    public static string[] Selected(string action) => action switch
    {
        Restock => ["inventory/0"],
        Ap or Stars => ["materia/0"],
        Recover or Level => ["character/1"],
        _ => throw new ArgumentException("Unknown action.", nameof(action)),
    };
}
