using System.Collections.Frozen;
using System.Text.Json;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal static class ItemNames
{
    private static readonly FrozenDictionary<string, FrozenDictionary<uint, string>> Names = Load();

    public static string Lookup(Ff7Schema schema, bool materia, uint id)
    {
        var table = (schema.IsRemake ? "remake" : "rebirth") + (materia ? "Materia" : "Items");
        return Names[table].TryGetValue(id, out var name) ? name : $"Unknown ID 0x{id:X}";
    }

    public static bool Contains(Ff7Schema schema, bool materia, uint id)
    {
        var table = (schema.IsRemake ? "remake" : "rebirth") + (materia ? "Materia" : "Items");
        return Names.TryGetValue(table, out var values) && values.ContainsKey(id);
    }

    public static IReadOnlyList<KeyValuePair<uint, string>> Entries(Ff7Schema schema, bool materia)
    {
        var table = (schema.IsRemake ? "remake" : "rebirth") + (materia ? "Materia" : "Items");
        return Names.TryGetValue(table, out var values) ? [.. values.OrderBy(pair => pair.Key)] : [];
    }

    private static FrozenDictionary<string, FrozenDictionary<uint, string>> Load()
    {
        using var stream = typeof(ItemNames).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.FinalFantasy7.Resources.names.json")
            ?? throw new InvalidOperationException("Embedded FFVII name data is missing.");
        var tables = JsonSerializer.Deserialize<Dictionary<string, Dictionary<uint, string>>>(stream)
            ?? throw new InvalidOperationException("Embedded FFVII name data is invalid.");
        return tables.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenDictionary(), StringComparer.Ordinal);
    }
}
