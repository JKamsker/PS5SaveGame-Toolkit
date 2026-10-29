using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk;

/// <summary>
/// Non-owning composition adapter between one document's local identities and its group namespace.
/// It dispatches only to that member. The group retains transaction, dependency, serialization,
/// snapshot and disposal policy; replace member Revert tokens with whole-group snapshots.
/// </summary>
/// <remarks>Does not synchronize member access. Serialize calls with the owning group and its policy callbacks.
/// Input views/plans must be finite and remain unchanged while mapping. Host limits still apply.</remarks>
public sealed class DocumentMemberAdapter
{
    private readonly DocumentMemberIdentityPolicy _policy;
    private readonly DocumentMemberViewMapper _views;

    /// <summary>Create an adapter for a stable role and the current member model.</summary>
    /// <exception cref="ArgumentNullException">The document or role is null.</exception>
    /// <exception cref="ArgumentException">The role is blank or a required policy callback is null.</exception>
    public DocumentMemberAdapter(string role, ISaveDocument document, DocumentMemberIdentityPolicy? identityPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(document);
        Role = role;
        Document = document;
        _policy = identityPolicy ?? new();
        _policy.Validate(nameof(identityPolicy));
        _views = new(this, _policy);
    }

    /// <summary>Stable member role. Compared ordinally by the default identity policy.</summary>
    public string Role { get; }

    /// <summary>Current member model. Lifetime and snapshots belong to the containing group.</summary>
    public ISaveDocument Document { get; }

    /// <summary>Qualify an opaque member-local identity. No implicit escaping is performed.</summary>
    public string Prefix(string id) => _policy.ToGroupId(Role, id);

    /// <summary>Whether the identity policy recognizes this group identity as belonging to this member.</summary>
    public bool Owns(string id) => _policy.TryToLocalId(Role, id) is not null;

    /// <summary>Remove this member's namespace; throw KeyNotFoundException for a foreign identity.</summary>
    public string Local(string id) => _policy.TryToLocalId(Role, id)
        ?? throw new KeyNotFoundException($"Unknown group identity for member '{Role}'.");

    /// <summary>Recursively map views. Action parameter names and reference/image asset IDs stay local.</summary>
    public ViewNode Node(ViewNode node, string scope = "view") => _views.Node(node, scope);

    /// <summary>Map collection metadata, including explicit title and column policy.</summary>
    public QueryableCollection Collection(QueryableCollection collection) => _views.Collection(collection);

    /// <summary>Map a query page's identity, row keys and cells, preserving paging/count/cursor metadata.</summary>
    public QueryableCollectionPage Page(QueryableCollectionPage page) => _views.Page(page);

    /// <summary>Freshly mapped member collection metadata, or empty if the member is not queryable.</summary>
    public IReadOnlyList<QueryableCollection> Collections => Document is IQueryableDocument queryable
        ? queryable.Collections.Select(Collection).ToArray() : [];

    /// <summary>Read a group-qualified field through its member-local binding.</summary>
    public FieldValue GetValue(string valueId) => Document.GetValue(Local(valueId));

    /// <summary>
    /// Resolves explicitly requested member metadata against the current member model and qualifies
    /// both field and canonical identities. Unknown IDs are omitted by the member contract; paths
    /// are mapped only when the plugin published a real navigation path.
    /// </summary>
    public IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        ArgumentNullException.ThrowIfNull(valueIds);
        if (Document is not IFieldMetadataDocument metadata) return [];
        var localIds = valueIds.Select(id => _policy.TryToLocalId(Role, id))
            .Where(id => id is not null).Select(id => id!).ToArray();
        if (localIds.Length == 0) return [];
        return metadata.ResolveFieldMetadata(Array.AsReadOnly(localIds)).Select(field => field with
        {
            ValueId = Prefix(field.ValueId),
            CanonicalValueId = Prefix(string.IsNullOrEmpty(field.CanonicalValueId) ? field.ValueId : field.CanonicalValueId),
            Path = field.Path is { } path ? Array.AsReadOnly(_policy.NavigationPath(Role, path).ToArray()) : null,
        }).ToArray();
    }

    /// <summary>Expand a group-qualified lazy section and map all returned children.</summary>
    public IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        var local = Local(lazyId);
        return Document.ExpandNode(local).Select((node, index) => Node(node, _policy.ExpansionScope(local, index))).ToArray();
    }

    /// <summary>Read bytes through a group-qualified blob ID; offsets and lengths are unchanged.</summary>
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => Document.ReadBlob(Local(blobId), offset, count);

    /// <summary>Dispatch an unchanged query to the local collection and map its result.</summary>
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var local = Local(collectionId);
        return Page(Queryable.Query(local, query));
    }

    /// <summary>
    /// Validate every identity before dispatching one member's edits. Preserve ordered duplicates.
    /// This is not a cross-member transaction; the group captures and restores its complete state.
    /// </summary>
    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => ToGroupOutcome(Document.ApplyEdits(
        edits.Select(edit => edit with { ValueId = Local(edit.ValueId) }).ToArray()));

    /// <summary>Dispatch an action with unchanged local parameter keys and qualify reported changes.</summary>
    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        ToGroupOutcome(Document.InvokeAction(Local(actionId), parameters));

    /// <summary>Localize a resolved F6 scope using the explicit member policy.</summary>
    public ActionScope ToLocalScope(ActionScope scope) => _policy.LocalScope is { } localScope ? localScope(this, scope) : scope with
    {
        RowKeys = scope.RowKeys?.Select(Local).ToArray(),
        QueryScope = scope.QueryScope is { } query ? query with
        {
            CollectionId = Local(query.CollectionId), ExcludedRowKeys = query.ExcludedRowKeys.Select(Local).ToArray(),
        } : null,
    };

    /// <summary>Dispatch a local preview, then qualify change, skipped-row and nonempty error identities.</summary>
    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var local = Local(actionId);
        return ToGroupPlan(Planning.PlanAction(local, parameters, ToLocalScope(scope)));
    }

    /// <summary>Localize a reviewed plan and scope before member revalidation/application.</summary>
    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var local = Local(actionId);
        var localScope = ToLocalScope(scope);
        var localPlan = ToLocalPlan(plan);
        return ToGroupOutcome(Planning.ApplyActionPlan(local, parameters, localScope, localPlan));
    }

    /// <summary>Qualify a local plan. A skipped-row key is opaque and uses the ordinary identity policy.</summary>
    public ActionPlan ToGroupPlan(ActionPlan plan) => MapPlan(plan, Prefix, MapGroupPath);

    /// <summary>Invert plan identity mapping, preserving empty general errors and every plan flag.</summary>
    public ActionPlan ToLocalPlan(ActionPlan plan) => MapPlan(plan, Local, MapLocalPath);

    /// <summary>Qualify changes and errors; retain member warnings, flags and the member-local Revert token.</summary>
    public EditOutcome ToGroupOutcome(EditOutcome outcome) => outcome with
    {
        Changes = outcome.Changes.Select(change => change with { ValueId = Prefix(change.ValueId) }).ToArray(),
        Errors = outcome.Errors.Select(error => MapError(error, Prefix)).ToArray(),
    };

    /// <summary>Qualify changes; retain the message, warnings, flags and member-local Revert token.</summary>
    public ActionOutcome ToGroupOutcome(ActionOutcome outcome) => outcome with
    {
        Changes = outcome.Changes.Select(change => change with { ValueId = Prefix(change.ValueId) }).ToArray(),
    };

    private static ActionPlan MapPlan(ActionPlan plan, Func<string, string> map,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> mapPath) => plan with
    {
        Changes = plan.Changes.Select(change => change with
        {
            ValueId = map(change.ValueId),
            Effect = change.Effect is { Path: { } path } effect
                ? effect with { Path = Array.AsReadOnly(mapPath(path).ToArray()) }
                : change.Effect,
        }).ToArray(),
        SkippedRows = plan.SkippedRows.Select(row => row with { RowKey = map(row.RowKey) }).ToArray(),
        Errors = plan.Errors.Select(error => MapError(error, map)).ToArray(),
    };

    private IReadOnlyList<string> MapGroupPath(IReadOnlyList<string> path)
    {
        var mapped = _policy.NavigationPath(Role, path);
        if (!path.SequenceEqual(_policy.LocalNavigationPath(Role, mapped), StringComparer.Ordinal))
            throw new InvalidOperationException($"Navigation path codec is not stable for member '{Role}'.");
        return mapped;
    }

    private IReadOnlyList<string> MapLocalPath(IReadOnlyList<string> path)
    {
        var mapped = _policy.LocalNavigationPath(Role, path);
        if (!path.SequenceEqual(_policy.NavigationPath(Role, mapped), StringComparer.Ordinal))
            throw new InvalidOperationException($"Navigation path codec is not stable for member '{Role}'.");
        return mapped;
    }

    private static FieldError MapError(FieldError error, Func<string, string> map) =>
        error.ValueId.Length == 0 ? error : error with { ValueId = map(error.ValueId) };

    private IQueryableDocument Queryable => Document as IQueryableDocument
        ?? throw new NotSupportedException("This member does not support collection queries.");

    private IActionPlanningDocument Planning => Document as IActionPlanningDocument
        ?? throw new NotSupportedException("This member does not support action planning.");
}
