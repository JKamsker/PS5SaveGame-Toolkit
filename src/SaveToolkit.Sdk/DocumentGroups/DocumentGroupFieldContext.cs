namespace SaveToolkit.Sdk;

/// <summary>Authoritative member ownership for an explicitly requested group field identity.</summary>
/// <param name="ValueId">The exact group-qualified field identity that was requested.</param>
/// <param name="Role">The selected member role that owns the identity.</param>
/// <param name="RelativePath">The selected member's save-set relative path.</param>
/// <param name="Provenance">Optional host or plugin provenance; null means it was not published.</param>
public sealed record DocumentGroupFieldContext(string ValueId, string Role, string RelativePath,
    string? Provenance = null);

/// <summary>
/// Optional bounded context projection for grouped semantic review. Implementations must return
/// context only for the requested identities and must not enumerate an entire document catalog.
/// </summary>
public interface IGroupedFieldContext
{
    /// <summary>Resolve only the supplied group-qualified identities; unknown IDs are omitted.</summary>
    IReadOnlyList<DocumentGroupFieldContext> ResolveFieldContext(IReadOnlyList<string> valueIds);
}
