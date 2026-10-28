using System.Globalization;

namespace SaveToolkit.Plugins.TalesOfArise;

internal static class AriseSchema
{
    // Schema identifiers are not payload checksums. Names/order are documented by save-converters.
    public static IReadOnlyList<SectionSchema> Sections { get; } = Array.AsReadOnly(Read("Sections")
        .Select(row => new SectionSchema(row[0], Hex(row[1]), uint.Parse(row[2], CultureInfo.InvariantCulture), checked((int)Hex(row[3]))))
        .ToArray());

    public static string? ConsumableName(ushort id) => Consumables.GetValueOrDefault(id);

    public static IReadOnlyList<string> CharacterNames { get; } = Array.AsReadOnly(Read("Characters").Select(row => row[1]).ToArray());

    private static IReadOnlyDictionary<ushort, string> Consumables { get; } = Read("Consumables")
        .ToDictionary(row => ushort.Parse(row[0], CultureInfo.InvariantCulture), row => row[1]);

    private static IEnumerable<string[]> Read(string name)
    {
        using var stream = typeof(AriseSchema).Assembly.GetManifestResourceStream($"SaveToolkit.Plugins.TalesOfArise.Resources.{name}.csv")
            ?? throw new InvalidOperationException($"Missing Tales of Arise schema resource {name}.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length != 0)
            {
                yield return line.Split(',');
            }
        }
    }

    private static uint Hex(string value) => uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

internal sealed record SectionSchema(string Name, uint Id, uint Version, int Size);
