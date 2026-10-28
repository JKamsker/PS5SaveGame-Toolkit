using System.Collections.Frozen;
using System.Globalization;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal static class FaithItemNames
{
    // Immutable reference data; read only our own embedded resource, never the file system.
    private static readonly FrozenDictionary<int, string> Names = Load();

    public static string? Find(int id) => Names.GetValueOrDefault(id);

    private static FrozenDictionary<int, string> Load()
    {
        using var stream = typeof(FaithItemNames).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.FinalFantasy16.Resources.ResourceItems.tsv")
            ?? throw new InvalidOperationException("Embedded FFXVI resource item names are missing.");
        using var reader = new StreamReader(stream);
        var names = new Dictionary<int, string>();
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf('\t');
            names.Add(int.Parse(line.AsSpan(0, separator), CultureInfo.InvariantCulture), line[(separator + 1)..]);
        }

        return names.ToFrozenDictionary();
    }
}
