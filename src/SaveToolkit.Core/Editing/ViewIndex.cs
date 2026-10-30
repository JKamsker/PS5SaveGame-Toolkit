using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Editing;

/// <summary>
/// Combines optional canonical registrations with view-learned fields and actions, so validation
/// never trusts browser-supplied types, ranges or binding policy.
/// </summary>
public sealed class ViewIndex
{
    private readonly FieldMetadataIndex _metadata;
    public ViewIndex() : this(null) { }
    public ViewIndex(ISaveDocument? document) => _metadata = new FieldMetadataIndex(document);
    private readonly Dictionary<string, FieldSpec> _specs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FieldNode> _fields = new(StringComparer.Ordinal);
    private readonly HashSet<string> _annotationConflicts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _registeredFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActionNode> _actions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lazyIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _blobIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _assetIds = new(StringComparer.Ordinal);
    private readonly SelectionBindingIndex _selections = new();

    private ViewIndex(ViewIndex source, CancellationToken cancellationToken)
    {
        _metadata = source._metadata.Copy(cancellationToken);
        _selections = source._selections.Copy(cancellationToken);
        Copy(source._specs, _specs, cancellationToken);
        Copy(source._fields, _fields, cancellationToken);
        foreach (var id in source._annotationConflicts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _annotationConflicts.Add(id);
        }
        foreach (var id in source._registeredFields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _registeredFields.Add(id);
        }
        Copy(source._actions, _actions, cancellationToken);
        foreach (var (from, to) in new[] { (source._lazyIds, _lazyIds), (source._blobIds, _blobIds), (source._assetIds, _assetIds) })
        {
            to.EnsureCapacity(from.Count);
            foreach (var id in from)
            {
                cancellationToken.ThrowIfCancellationRequested();
                to.Add(id);
            }
        }
    }

    internal ViewIndex Copy(CancellationToken cancellationToken) => new(this, cancellationToken);

    /// <summary>
    /// Identifies expansion output that cannot change the index. The caller may return this output
    /// without staging a copy because these node kinds have no bindings, metadata, or selection state.
    /// Unknown node types stay on the transactional path so future SDK nodes remain safe by default.
    /// </summary>
    internal static bool IsIndexNeutral(IEnumerable<ViewNode> nodes)
    {
        foreach (var node in nodes)
            if (!IsIndexNeutral(node)) return false;
        return true;
    }

    private static bool IsIndexNeutral(ViewNode node) => node switch
    {
        TextNode or StatsNode => true,
        SectionNode { LazyId: null } section => IsIndexNeutral(section.Children),
        TabsNode tabs => tabs.Tabs.All(tab => IsIndexNeutral(tab.Children)),
        _ => false,
    };

    private static void Copy<T>(Dictionary<string, T> source, Dictionary<string, T> destination, CancellationToken cancellationToken)
    {
        destination.EnsureCapacity(source.Count);
        foreach (var entry in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>Advisory selection-binding diagnostics for the currently indexed view, including loaded expansions/pages.</summary>
    public IReadOnlyList<string> SelectionDiagnostics => _selections.Diagnostics();

    public IReadOnlyDictionary<string, FieldSpec> Specs => _specs;

    public FieldSpec? Spec(string valueId)
    {
        _ = Metadata(valueId);
        return ResolvedSpec(valueId);
    }

    internal FieldSpec? ResolvedSpec(string valueId) => ResolvedMetadata(valueId)?.Spec
        ?? (_registeredFields.Contains(valueId) ? null : _specs.GetValueOrDefault(valueId));

    internal FieldNode? ShownField(string valueId)
    {
        if (_annotationConflicts.Contains(valueId))
            throw new InvalidOperationException($"Rendered canonical field annotations conflict for field '{valueId}'.");
        return _fields.GetValueOrDefault(valueId);
    }

    internal bool HasRenderedField(string valueId) => ShownField(valueId) is not null;
    internal bool RequiresMetadata(string valueId) => _registeredFields.Contains(valueId);

    /// <summary>
    /// Resolves presentations after indexing a tree. Uses only its loaded canonical fields and targeted
    /// metadata from the same document; never expands lazy sections or queries additional pages.
    /// Missing targets remain unavailable FieldReferenceNodes. Does not register resolved projections.
    /// </summary>
    public ViewNode ResolveFieldReferences(ISaveDocument document, ViewNode node, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new FieldReferenceProjection(document, this, cancellationToken).Resolve(node);
    }

    public FieldMetadata? Metadata(string valueId)
    {
        if (!_metadata.Enabled) return null;
        _ = _metadata.Resolve(valueId);
        return ResolvedMetadata(valueId);
    }

    internal FieldMetadata? ResolvedMetadata(string valueId)
    {
        var field = _metadata.Cached(valueId);
        if (field is not null)
        {
            RequireAgreement(valueId, field.Spec);
            _registeredFields.Add(valueId);
        }
        return field;
    }

    internal void SetMetadataBudget(long? bytes) => _metadata.SetBudget(bytes);

    public bool HasFieldMetadata => _metadata.Enabled;

    public void ResolveMetadata(IEnumerable<string> valueIds)
        => ResolveMetadata(valueIds, CancellationToken.None);

    internal void ResolveMetadata(IEnumerable<string> valueIds, CancellationToken cancellationToken,
        int maxInitialIds = FieldMetadataLimits.MaxClosureIds)
    {
        if (!_metadata.Enabled) return;
        var ids = valueIds.Distinct(StringComparer.Ordinal).Take(maxInitialIds + 1).ToArray();
        _metadata.ResolveAll(ids, maxInitialIds, cancellationToken);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = ResolvedMetadata(id);
        }
    }

    private void RequireAgreement(string id, FieldSpec spec)
    {
        if (_specs.TryGetValue(id, out var shown) && !FieldSpecMetadata.Agree(shown, spec))
            throw new InvalidOperationException($"Registered and rendered specs conflict for field '{id}'.");
    }

    public ActionNode? Action(string actionId) => _actions.GetValueOrDefault(actionId);

    public bool KnowsLazy(string lazyId) => _lazyIds.Contains(lazyId);

    public bool KnowsBlob(string blobId) => _blobIds.Contains(blobId);

    public bool KnowsAsset(string assetId) => _assetIds.Contains(assetId);

    /// <summary>Every value, action and lazy id currently indexed (the ids clients send back).</summary>
    public IEnumerable<string> BindingIds => _specs.Keys.Concat(_metadata.Ids).Concat(_actions.Keys).Concat(_lazyIds);

    public bool KnowsBinding(string id) => _specs.ContainsKey(id) || _actions.ContainsKey(id) || _lazyIds.Contains(id) || Metadata(id) is not null;

    public void Clear()
    {
        _specs.Clear();
        _fields.Clear();
        _annotationConflicts.Clear();
        _registeredFields.Clear();
        _metadata.Clear();
        _actions.Clear();
        _lazyIds.Clear();
        _blobIds.Clear();
        _assetIds.Clear();
        _selections.Clear();
    }

    /// <summary>Indexes a node tree. Throws <see cref="InvalidOperationException"/> on malformed views.</summary>
    public void Add(ViewNode node)
        => Add(node, CancellationToken.None);

    internal void Add(ViewNode node, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _selections.Add(node);
        switch (node)
        {
            case SectionNode s:
                if (s.LazyId is { } lazy)
                {
                    _lazyIds.Add(lazy);
                }
                else
                {
                    AddAll(s.Children, cancellationToken);
                }

                break;
            case TabsNode t:
                foreach (var tab in t.Tabs)
                {
                    AddAll(tab.Children, cancellationToken);
                }

                break;
            case FieldNode f:
                if (string.IsNullOrEmpty(f.ValueId))
                {
                    throw new InvalidOperationException($"Field '{f.Label}' has no value id.");
                }

                if (_fields.TryGetValue(f.ValueId, out var learned) &&
                    (learned.Warning != f.Warning || learned.Confidence != f.Confidence || learned.Description != f.Description))
                    _annotationConflicts.Add(f.ValueId);

                if (Metadata(f.ValueId) is { } registered)
                {
                    if (!FieldSpecMetadata.Agree(registered.Spec, f.Spec))
                        throw new InvalidOperationException($"Registered and rendered specs conflict for field '{f.ValueId}'.");
                    _specs[f.ValueId] = registered.Spec;
                }
                else _specs[f.ValueId] = f.Spec;
                _fields[f.ValueId] = f;
                break;
            case FieldReferenceNode reference:
                reference.Validate();
                // Presentation never publishes a binding or a spec, even when its target is missing.
                break;
            case TableNode table:
                var keys = table.Columns.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var row in table.Rows)
                {
                    foreach (var (key, cell) in row.Cells)
                    {
                        if (!keys.Contains(key))
                        {
                            throw new InvalidOperationException($"Table row '{row.Key}' has a cell for unknown column '{key}'.");
                        }

                        Add(cell, cancellationToken);
                    }
                }

                break;
            case QueryableTableNode:
                // Rows are indexed when a bounded page is queried.
                break;
            case GraphNode graph:
                SpatialViewValidation.Validate(graph);
                break;
            case MapNode map:
                SpatialViewValidation.Validate(map);
                break;
            case ActionNode a:
                _actions[a.ActionId] = a;
                break;
            case HexNode h:
                _blobIds.Add(h.BlobId);
                break;
            case ImageNode i:
                _assetIds.Add(i.AssetId);
                break;
        }
    }

    public void AddAll(IEnumerable<ViewNode> nodes)
        => AddAll(nodes, CancellationToken.None);

    internal void AddAll(IEnumerable<ViewNode> nodes, CancellationToken cancellationToken)
    {
        foreach (var n in nodes)
        {
            Add(n, cancellationToken);
        }
    }
}
