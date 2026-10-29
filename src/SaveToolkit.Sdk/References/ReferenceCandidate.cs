namespace SaveToolkit.Sdk.References;

/// <summary>Availability in this build/save and eligibility for this field. Reasons explain restrictions.</summary>
public sealed record ReferenceEligibility
{
    /// <summary>Whether this candidate is available in the current build or save.</summary>
    public bool Available { get; init; } = true;
    /// <summary>Whether this candidate is suitable for this field under plugin policy.</summary>
    public bool Eligible { get; init; } = true;
    /// <summary>User-facing reasons for restrictions or uncertainty.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];
    /// <summary>True only when the candidate is both available and eligible.</summary>
    public bool Selectable => Available && Eligible;
}

/// <summary>A raw id plus display metadata and resolved record/icon provenance. Unknown ids have no label.</summary>
public sealed record ReferenceCandidate(string RawId)
{
    /// <summary>Resolved display name, or null for an unknown raw ID.</summary>
    public string? Label { get; init; }
    /// <summary>Optional catalog description.</summary>
    public string? Description { get; init; }
    /// <summary>Optional catalog category.</summary>
    public string? Category { get; init; }
    /// <summary>Optional pack-qualified local image ID.</summary>
    public string? AssetId { get; init; }
    /// <summary>Optional per-record upstream source URL.</summary>
    public string? Source { get; init; }
    /// <summary>Provenance of the resolved record dataset.</summary>
    public ReferenceDatasetInfo? Dataset { get; init; }
    /// <summary>Separate provenance of the image dataset, when available.</summary>
    public ReferenceDatasetInfo? AssetDataset { get; init; }
    /// <summary>Resolved field-specific selection policy and reasons.</summary>
    public ReferenceEligibility Eligibility { get; init; } = new();
}

/// <summary>A bounded, field-specific page. Revision binds candidates to the view used to query them.</summary>
public sealed record ReferenceCandidatePage(
    string CatalogId,
    IReadOnlyList<ReferenceCandidate> Items,
    int Total,
    int Offset,
    int Limit,
    IReadOnlyList<string> Categories,
    long Revision)
{
    /// <summary>Sort keys supported by the pinned catalog.</summary>
    public IReadOnlyList<ReferenceSortKey> SortKeys { get; init; } = [];
}
