namespace SaveToolkit.Sdk.References;

/// <summary>Availability checks for a reference-backed icon.</summary>
public enum ReferenceIconAvailability
{
    /// <summary>Require both local image bytes and image provenance (the default).</summary>
    RequireBytesAndProvenance,

    /// <summary>Require image provenance, even if local bytes are unavailable.</summary>
    RequireProvenance,

    /// <summary>
    /// Permit any non-null asset id without bytes/provenance. Without provenance, exact alt text is required.
    /// </summary>
    AssetIdOnly,
}

/// <summary>Explicit compatibility policies for icon rendering through <see cref="ReferenceViews"/>.</summary>
public sealed record ReferenceIconOptions
{
    /// <summary>
    /// Caller-owned alt text, used verbatim (including empty text), without appending attribution.
    /// Null composes the usual label and separate record/image attribution, which requires provenance
    /// even under <see cref="ReferenceIconAvailability.AssetIdOnly"/>. Include any required notices here.
    /// </summary>
    public string? ExactAltText { get; init; }

    /// <summary>
    /// Defaults to requiring local bytes and image provenance. Every policy requests asset provenance
    /// and resolves permitted assets to register Sources, including when exact alt text is supplied.
    /// </summary>
    public ReferenceIconAvailability Availability { get; init; } = ReferenceIconAvailability.RequireBytesAndProvenance;
}
