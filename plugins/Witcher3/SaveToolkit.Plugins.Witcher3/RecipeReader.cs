using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record SavedRecipe(string Kind, string NativeId);

internal static class RecipeReader
{
    private const int MaximumRecipes = 20_000;

    public static IReadOnlyList<SavedRecipe> Read(Sav3Node entity, Sav3Tree tree, byte[] data, IReadOnlyList<string> names)
    {
        var result = new List<SavedRecipe>();
        foreach (var name in new[] { "craftingSchematics", "alchemyRecipes" })
        {
            var arrays = entity.Children.Select(id => tree.Nodes[id]).Where(n => n.Tag == "PORP" && n.Name == name).ToArray();
            if (arrays.Length == 0) { throw new SaveFormatException($"No explicit saved {name} array; an empty recipe list cannot be inferred."); }
            if (arrays.Length != 1 || arrays[0].Type != "array:2,0,CName")
            {
                throw new SaveFormatException($"Unsupported saved {name} array.");
            }

            foreach (var id in ReadNames(BinaryData.Slice(data, arrays[0].ValueOffset, arrays[0].ValueSize), names))
            {
                result.Add(new SavedRecipe(name, id));
            }
        }

        return result;
    }

    internal static IReadOnlyList<string> ReadNames(ReadOnlySpan<byte> raw, IReadOnlyList<string> names, string context = "Recipe")
    {
        var count = BinaryData.Int32(raw, 0);
        if (count < 0 || count > MaximumRecipes || raw.Length != sizeof(int) + (long)count * sizeof(ushort))
        {
            throw new SaveFormatException($"{context} count disagrees with its bounded name array.");
        }

        var result = new List<string>(count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var index = BinaryData.UInt16(raw, sizeof(int) + i * sizeof(ushort));
            if (index < 1 || index > names.Count || !seen.Add(names[index - 1]))
            {
                throw new SaveFormatException($"Invalid or duplicate {context.ToLowerInvariant()} name index.");
            }

            result.Add(names[index - 1]);
        }

        return result;
    }
}
