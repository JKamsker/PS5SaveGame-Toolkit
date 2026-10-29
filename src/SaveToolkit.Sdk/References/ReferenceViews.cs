using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk.References;

/// <summary>Local reference-backed view helpers that retain record and asset attribution.</summary>
public static class ReferenceViews
{
    /// <summary>
    /// Resolves local image availability and provenance (registering the image dataset in Sources).
    /// Includes separate record/image attribution. The plugin supplies any identity/evidence caveat in label.
    /// </summary>
    public static ViewNode Icon(IReferenceContext context, ReferenceRecord? record, string? label = null, ViewNode? fallback = null) =>
        Icon(context, record, label, fallback, options: null);

    /// <summary>
    /// Resolves an icon with explicit compatibility policies. Null options retain the default behavior.
    /// Label and fallback are explicit to preserve existing calls, including a null third argument.
    /// Requests image provenance and resolves assets to register Sources. Exact alt text owns all visible notices;
    /// composed alt text still requires provenance, regardless of availability policy.
    /// </summary>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The availability policy is not a defined enum value.</exception>
    public static ViewNode Icon(IReferenceContext context, ReferenceRecord? record, string? label,
        ViewNode? fallback, ReferenceIconOptions? options)
    {
        ArgumentNullException.ThrowIfNull(context);
        var availability = options?.Availability ?? ReferenceIconAvailability.RequireBytesAndProvenance;
        if (!Enum.IsDefined(availability))
            throw new ArgumentOutOfRangeException(nameof(options), availability, "Unknown icon availability policy.");
        if (record?.AssetId is not { } asset)
            return MissingIcon(fallback);

        var provenance = context.AssetProvenance(asset);
        if (provenance is null && availability != ReferenceIconAvailability.AssetIdOnly)
            return MissingIcon(fallback);

        // Resolving an asset registers its dataset in Sources, even when missing bytes are allowed.
        var image = context.GetAsset(asset);
        if (image is null && availability == ReferenceIconAvailability.RequireBytesAndProvenance)
            return MissingIcon(fallback);

        var alt = options?.ExactAltText;
        if (alt is null)
        {
            if (provenance is null)
                return MissingIcon(fallback);
            alt = $"{label ?? record.Name}. {record.AttributionText}. Icon: {provenance.Attribution.ToShortText()}";
        }
        return new ImageNode(asset, alt);
    }

    private static ViewNode MissingIcon(ViewNode? fallback) => fallback ?? new TextNode("No reference icon");
}
