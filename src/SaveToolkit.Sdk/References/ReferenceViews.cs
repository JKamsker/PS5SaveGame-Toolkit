using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk.References;

/// <summary>Local reference-backed view helpers that retain record and asset attribution.</summary>
public static class ReferenceViews
{
    /// <summary>
    /// Resolves local image availability and provenance (registering the image dataset in Sources).
    /// Includes separate record/image attribution. The plugin supplies any identity/evidence caveat in label.
    /// </summary>
    public static ViewNode Icon(IReferenceContext context, ReferenceRecord? record, string? label = null, ViewNode? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (record?.AssetId is not { } asset || context.AssetProvenance(asset) is not { } provenance || context.GetAsset(asset) is null)
            return fallback ?? new TextNode("No reference icon");
        return new ImageNode(asset, $"{label ?? record.Name}. {record.AttributionText}. Icon: {provenance.Attribution.ToShortText()}");
    }
}
