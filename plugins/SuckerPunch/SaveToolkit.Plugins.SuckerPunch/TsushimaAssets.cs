using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Resolves cached F2 artwork and its provenance without decoding images or performing I/O.</summary>
internal static class TsushimaAssets
{
    public const string IconColumn = "icon";
    public const string MapAssetId = "suckerpunch.tsushima:maps/tsushima.webp";
    public const int MapWidth = 1152;
    public const int MapHeight = 2048;

    public static ViewNode Icon(IReferenceContext references, ReferenceRecord? record) =>
        record?.AssetId is { } id && references.GetAsset(id) is not null && references.AssetProvenance(id) is not null
            ? new ImageNode(id, record.Name)
            : new TextNode("—") { Tone = Tone.Muted };

    public static string Attribution(IReferenceContext references, ReferenceRecord? record) =>
        record?.AssetId is { } id && references.AssetProvenance(id) is { } provenance
            ? " Artwork: " + provenance.Attribution.ToShortText()
            : "";

    public static MapBackground? Background(IReferenceContext references) =>
        references.GetAsset(MapAssetId) is not null && references.AssetProvenance(MapAssetId) is { } provenance
            ? new MapBackground(MapAssetId,
                "Tsushima island reference artwork; shrine positions and saved-state alignment are unverified.",
                provenance.Attribution, EmbeddingPermitted: true)
            : null;
}
