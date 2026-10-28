namespace SaveToolkit.Sdk.References;

/// <summary>
/// One entry of a reference catalog, e.g. an item. Everything except <see cref="Key"/> and
/// <see cref="Name"/> is optional. <see cref="Dataset"/> resolves the record's provenance.
/// </summary>
/// <param name="Key">Stable raw id as the save stores it (decimal, hex or hash; compared ordinally).</param>
/// <param name="Name">Display name.</param>
public sealed record ReferenceRecord(string Key, string Name)
{
    public string? Category { get; init; }

    /// <summary>Game limit for quantities (e.g. max stack size), when known.</summary>
    public long? Limit { get; init; }

    public string? Description { get; init; }

    /// <summary>Pack-qualified asset id (<c>packId:path</c>); pass it to <see cref="IReferenceContext.GetAsset"/>.</summary>
    public string? AssetId { get; init; }

    /// <summary>Record-level source link (e.g. the wiki page), in addition to the dataset attribution.</summary>
    public string? Source { get; init; }

    /// <summary>Free-form extra columns (internal names, hashes, flags).</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = EmptyAttributes;

    /// <summary>Dataset the record came from, with its attribution.</summary>
    public required ReferenceDatasetInfo Dataset { get; init; }

    /// <summary>Attribution line for a field description: dataset attribution plus record source.</summary>
    public string AttributionText => Source is null
        ? $"Source: {Dataset.Attribution.ToShortText()}"
        : $"Source: {Source}; {Dataset.Attribution.ToShortText()}";

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes = new Dictionary<string, string>();
}
