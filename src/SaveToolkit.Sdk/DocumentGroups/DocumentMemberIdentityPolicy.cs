using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk;

/// <summary>
/// Pure namespace and presentation callbacks for a composed member. Defaults concatenate the role
/// and an opaque local ID with '/', without escaping or decoding either. Callbacks must be stable
/// across fresh group instances and must not mutate their arguments.
/// </summary>
/// <remarks>All callbacks except LocalScope are required. Captured state is caller-owned; init-only properties
/// do not make callbacks thread-safe. Mapping work is proportional to the supplied finite view/page/plan;
/// this helper adds no traversal or transaction budget.</remarks>
public sealed record DocumentMemberIdentityPolicy
{
    /// <summary>Map a local field, action, collection, lazy, blob or target ID into the group.</summary>
    public Func<string, string, string> ToGroupId { get; init; } = static (role, id) => role + "/" + id;

    /// <summary>
    /// Inverse of ToGroupId. Return null for an ID owned by another member; an empty local ID is valid.
    /// Custom escaping must be implemented here and in ToGroupId together.
    /// </summary>
    public Func<string, string, string?> TryToLocalId { get; init; } = static (role, id) =>
        id.StartsWith(role + "/", StringComparison.Ordinal) ? id[(role.Length + 1)..] : null;

    /// <summary>Compose a local query-row identity from collection ID and row key, before prefixing.</summary>
    public Func<string, string, string> QueryRowIdentity { get; init; } = static (_, key) => key;

    /// <summary>Compose a local eager-table row identity from traversal scope and row key.</summary>
    public Func<string, string, string> TableRowIdentity { get; init; } = static (scope, key) => scope + "/" + key;

    /// <summary>Traversal scope of an eager-table cell, given parent scope, row key and column key.</summary>
    public Func<string, string, string, string> TableCellScope { get; init; } = static (scope, row, column) => scope + "/" + row + "/" + column;

    /// <summary>Traversal scope of an expanded child, given the local lazy ID and child index.</summary>
    public Func<string, int, string> ExpansionScope { get; init; } = static (id, index) => id + "/" + index;

    /// <summary>Map navigation ancestors, given the member role and original path. Defaults to unchanged.</summary>
    public Func<string, IReadOnlyList<string>, IReadOnlyList<string>> NavigationPath { get; init; } = static (_, path) => path;

    /// <summary>Inverse of <see cref="NavigationPath"/> for action-effect descriptor paths.</summary>
    public Func<string, IReadOnlyList<string>, IReadOnlyList<string>> LocalNavigationPath { get; init; } = static (_, path) => path;

    /// <summary>Whether graph item IDs and both edge endpoints are prefixed together. Defaults to true.</summary>
    public bool PrefixGraphIds { get; init; } = true;

    /// <summary>Whether map marker IDs are prefixed. Target IDs are always mapped separately.</summary>
    public bool PrefixMapMarkerIds { get; init; } = true;

    /// <summary>Collection metadata title, given role and original title. View table titles stay unchanged.</summary>
    public Func<string, string, string> CollectionTitle { get; init; } = static (role, title) => role + ": " + title;

    /// <summary>Metadata-column policy, defaulting to unchanged; for example member-specific read-only flags. Page columns stay unchanged.</summary>
    public Func<ISaveDocument, QueryableColumn, QueryableColumn> CollectionColumn { get; init; } = static (_, column) => column;

    /// <summary>
    /// Optional full scope localization, including row validation against the current member model.
    /// Defaults to removing the member prefix from row keys, query collection and exclusions.
    /// Use the supplied adapter's Document so restored/reopened models are never captured accidentally.
    /// </summary>
    public Func<DocumentMemberAdapter, ActionScope, ActionScope>? LocalScope { get; init; }

    internal void Validate(string parameterName)
    {
        if (ToGroupId is null || TryToLocalId is null || QueryRowIdentity is null || TableRowIdentity is null
            || TableCellScope is null || ExpansionScope is null || NavigationPath is null || LocalNavigationPath is null
            || CollectionTitle is null || CollectionColumn is null)
            throw new ArgumentException("Identity policy callbacks must be non-null; only LocalScope may be null.", parameterName);
    }
}
