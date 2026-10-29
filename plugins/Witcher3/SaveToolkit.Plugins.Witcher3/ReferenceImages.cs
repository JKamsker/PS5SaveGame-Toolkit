using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

internal static class ReferenceImages
{
    public static ViewNode Cell(ReferenceRecord? record, Func<string, ReferenceDatasetInfo?>? provenance) =>
        record?.AssetId is { } asset && provenance?.Invoke(asset) is not null
            ? new ImageNode(asset, record.Name)
            : new TextNode("No reference image");

    public static IReadOnlyList<ViewNode> Sources(ReferenceRecord? record, Func<string, ReferenceDatasetInfo?>? provenance) =>
        record?.AssetId is { } asset && provenance?.Invoke(asset) is { } dataset
            ? [new TextNode($"Image source: {dataset.Attribution.ToShortText()}")]
            : [];
}
