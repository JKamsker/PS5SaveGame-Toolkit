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
    // PartyProfile body offsets, distinct from the character-status fields above.
    public const int CurrentCpOffset = 0x0C;
    public const int MaximumCpOffset = 0x0E;
    public const string CpSource = "https://fearlessrevolution.com/download/file.php?id=41559";
    public const string CpAttribution = "Tuuuup!, Tales of Arise-v9.CT (2022-04-13), retrieved 2026-10-29: " + CpSource + ". "
        + "No licence published; reused with attribution per owner decision 2026-10-28. "
        + "PC halfword layout independently inferred on PS5 from three slots and their identical backups (155/155), beside the existing gald mapping; no code port.";
    public const string CharacterAttribution = "Aselia Wiki contributors, CC BY-SA 3.0 (https://creativecommons.org/licenses/by-sa/3.0/), retrieved 2026-10-28: https://aselia.fandom.com/wiki/Template:ToA_Characters?oldid=300086. Status-index joins are inferred from the Sora3100 Arise.CT ordering (https://raw.githubusercontent.com/Sora3100/Tales_of_Cheat_Tables/c92871084d714d98b3854ba9fbb4e3f022145d7e/Arise.CT). No licence published; reused with attribution per owner decision 2026-10-28. This does not establish recruitment or PS5 identity.";
    public const string ConsumableAttribution = "Aselia Wiki contributors, CC BY-SA 3.0 (https://creativecommons.org/licenses/by-sa/3.0/), retrieved 2026-10-28: https://aselia.fandom.com/wiki/ToA_-_Consumables?oldid=382800. Numeric identities are legacy inferred mappings from the PS4 patch research attachment (https://web.save-editor.com/bbs/patchcode/ps4/dl.cgi?file=31135.txt); the wiki does not establish PS5 IDs. No licence published; reused with attribution per owner decision 2026-10-28.";
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
