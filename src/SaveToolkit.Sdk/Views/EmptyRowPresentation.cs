namespace SaveToolkit.Sdk.Views;

/// <summary>Optional presentation of explicitly marked empty slots; never changes saved data or row identities.</summary>
/// <param name="LabelColumnKey">Column whose compact cell displays "Empty slot"; the original remains in row details.</param>
public sealed record EmptyRowPresentation(string LabelColumnKey)
{
    /// <summary>Human plural label used by the visibility toggle.</summary>
    public string PluralLabel { get; init; } = "empty slots";

    /// <summary>Initially hides confirmed empty slots while retaining their data and stable identities.</summary>
    public bool HideByDefault { get; init; } = true;
}
