using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Local reference artwork and its separate publisher-media provenance.</summary>
internal static class AriseAssets
{
    public const string MapId = "talesofarise.catalog:maps/dahna.webp";
    public const string MapSource = "https://w.atwiki.jp/talesofdic/pages/18355.html";
    public const string ArteSource = "https://www.gamerguides.com/tales-of-arise/guide/characters/alphen/artes";
    public const string MediaLicense = "Rights reserved by Bandai Namco Entertainment; used with attribution per owner decision 2026-10-29";

    public static IReadOnlyList<string> DescriptionSources { get; } =
    [
        "https://aselia.fandom.com/wiki/Air_Thrust?oldid=377924#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Burning_Strike?oldid=375442#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Cross_Blade?oldid=351836#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Demon_Fang?oldid=383124#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/First_Aid?oldid=376219#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Guardian_Field?oldid=373692#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Heal?oldid=381008#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Healing_Circle?oldid=357897#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Rising_Wyvern?oldid=318812#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Sharpness?oldid=375106#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Slag_Assault?oldid=369723#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Sonic_Thrust?oldid=369706#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Spread?oldid=377919#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Steel?oldid=369704#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Swallow_Blade?oldid=357914#Tales_of_Arise",
        "https://aselia.fandom.com/wiki/Talon_Storm?oldid=383173#Tales_of_Arise",
    ];

    public static ViewNode Icon(IReferenceContext context, ReferenceRecord? record) =>
        ReferenceViews.Icon(context, record, label: null, fallback: new TextNode("—"), options: new()
        {
            Availability = ReferenceIconAvailability.AssetIdOnly,
            ExactAltText = record is null ? null
                : $"{record.Name}: {record.Attributes.GetValueOrDefault("iconRole") ?? "game icon"}. {MediaLicense}",
        });

    public static string Details(ReferenceRecord record) => record.Attributes.TryGetValue("iconSource", out var source)
        ? $"{record.Description}\n{record.AttributionText}\nIcon: {source}; {record.Attributes.GetValueOrDefault("iconRole")}. {MediaLicense}"
        : $"{record.Description}\n{record.AttributionText}";

    public static MapBackground? Background(IReferenceContext context)
    {
        if (context.GetAsset(MapId) is null || context.AssetProvenance(MapId) is not { } provenance)
        {
            return null;
        }

        return new MapBackground(MapId, "Dahna world-map story artwork; schematic markers are not geographic locations.",
            provenance.Attribution, EmbeddingPermitted: true);
    }
}
