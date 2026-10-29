namespace SaveToolkit.Sdk;

/// <summary>
/// Opt-in ownership of exact existing system files in the opened save directory.
/// Paths are directory-relative (for example, <c>sce_sys/param.sfo</c>).
/// </summary>
public interface IAdditionalOutputOwner
{
    /// <summary>
    /// Exact existing system-file paths this document may serialize in addition to its opened file.
    /// The host rejects missing, non-system, wildcard, sibling and duplicate paths.
    /// </summary>
    IReadOnlyList<string> AdditionalOutputPaths { get; }
}
