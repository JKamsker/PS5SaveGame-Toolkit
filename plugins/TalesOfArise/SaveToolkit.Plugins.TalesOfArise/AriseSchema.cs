using System.Globalization;
using System.Text.Json;

namespace SaveToolkit.Plugins.TalesOfArise;

internal static class AriseSchema
{
    public const int MainCharacterCount = 6;
    public const int LevelOffset = 0x04;
    public const int CurrentHpOffset = 0x08;
    public const int ExperienceOffset = 0x10;
    public const int SkillPointsOffset = 0x30;
    public const int BaseHpOffset = 0x50;
    public const string CharacterAttribution = "Aselia Wiki contributors, CC BY-SA 3.0 (https://creativecommons.org/licenses/by-sa/3.0/), retrieved 2026-10-28: https://aselia.fandom.com/wiki/Template:ToA_Characters?oldid=300086. Status-index joins are inferred independently, not wiki order.";
    public const string ConsumableAttribution = "Aselia Wiki contributors, CC BY-SA 3.0 (https://creativecommons.org/licenses/by-sa/3.0/), retrieved 2026-10-28: https://aselia.fandom.com/wiki/ToA_-_Consumables?oldid=382800. Numeric identities are legacy inferred mappings; the wiki does not establish PS5 IDs.";
    public const string SectionSource = "https://github.com/ItsAllAboutTheCode/save-converters/blob/663239063e7c1cc8550f4248f772ee9dd47ed5b0/save_convert/tales_of/arise/save_NOTES.md";
    public const string SectionAttribution = "Section labels: ItsAllAboutTheCode/save-converters (GPL-3.0 format research; no code port), retrieved 2026-10-28. " + SectionSource + ". IDs, sizes and versions measured independently on local PS5 samples.";

    public static IReadOnlyList<string> CatalogSources { get; } = ReadCatalogSources();

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

    private static IReadOnlyList<string> ReadCatalogSources()
    {
        using var stream = typeof(AriseSchema).Assembly.GetManifestResourceStream("refpacks/talesofarise.catalog/source-revisions.json")
            ?? throw new InvalidOperationException("Missing pinned Tales of Arise catalog sources.");
        using var document = JsonDocument.Parse(stream);
        return Array.AsReadOnly(document.RootElement.EnumerateArray().Select(source => source.GetProperty("url").GetString()!).ToArray());
    }
}

internal sealed record SectionSchema(string Name, uint Id, uint Version, int Size);
