namespace SaveToolkit.Sdk.References;

/// <summary>Availability in this build/save and eligibility for this field. Reasons explain restrictions.</summary>
public sealed record ReferenceEligibility
{
    public bool Available { get; init; } = true;
    public bool Eligible { get; init; } = true;
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public bool Selectable => Available && Eligible;
}

/// <summary>A raw id plus display metadata and resolved record/icon provenance. Unknown ids have no label.</summary>
public sealed record ReferenceCandidate(string RawId)
{
    public string? Label { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? AssetId { get; init; }
    public string? Source { get; init; }
    public ReferenceDatasetInfo? Dataset { get; init; }
    public ReferenceDatasetInfo? AssetDataset { get; init; }
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
    public IReadOnlyList<ReferenceSortKey> SortKeys { get; init; } = [];
}
