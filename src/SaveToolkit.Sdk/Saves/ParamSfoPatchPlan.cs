namespace SaveToolkit.Sdk.Saves;

/// <summary>Immutable proof that one existing UTF-8 SFO entry may be replaced.</summary>
public sealed record ParamSfoPatchPlan(
    string Key,
    ushort Format,
    int EntryIndex,
    int DataOffset,
    int OriginalLength,
    int MaximumLength,
    string OriginalText,
    string NewText,
    string OriginalFileSha256,
    string OriginalEntrySha256)
{
    /// <summary>Whether applying this plan leaves the source bytes unchanged.</summary>
    public bool IsNoOp => string.Equals(OriginalText, NewText, StringComparison.Ordinal);
}
