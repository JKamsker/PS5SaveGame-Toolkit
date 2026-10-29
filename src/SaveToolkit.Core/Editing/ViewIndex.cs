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
    private readonly Dictionary<string, ActionNode> _actions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lazyIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _blobIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _assetIds = new(StringComparer.Ordinal);
    private readonly SelectionBindingIndex _selections = new();

    /// <summary>Advisory selection-binding diagnostics for the currently indexed view, including loaded expansions/pages.</summary>
    public IReadOnlyList<string> SelectionDiagnostics => _selections.Diagnostics();

    public IReadOnlyDictionary<string, FieldSpec> Specs => _specs;

    public FieldSpec? Spec(string valueId)
    {
        _ = Metadata(valueId);
        return ResolvedSpec(valueId);
    }

    internal FieldSpec? ResolvedSpec(string valueId) => ResolvedMetadata(valueId)?.Spec ?? _specs.GetValueOrDefault(valueId);

    public FieldMetadata? Metadata(string valueId)
    {
        if (!_metadata.Enabled) return null;
        _ = _metadata.Resolve(valueId);
        return ResolvedMetadata(valueId);
    }

    internal FieldMetadata? ResolvedMetadata(string valueId)
    {
        var field = _metadata.Cached(valueId);
        if (field is not null) RequireAgreement(valueId, field.Spec);
        return field;
    }

    public bool HasFieldMetadata => _metadata.Enabled;

    public void ResolveMetadata(IEnumerable<string> valueIds)
    {
        if (!_metadata.Enabled) return;
        var ids = valueIds.Distinct(StringComparer.Ordinal).ToArray();
        _metadata.ResolveAll(ids);
        foreach (var id in ids) _ = ResolvedMetadata(id);
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
        _metadata.Clear();
        _actions.Clear();
        _lazyIds.Clear();
        _blobIds.Clear();
        _assetIds.Clear();
        _selections.Clear();
    }

    /// <summary>Indexes a node tree. Throws <see cref="InvalidOperationException"/> on malformed views.</summary>
    public void Add(ViewNode node)
    {
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
                    AddAll(s.Children);
                }

                break;
            case TabsNode t:
                foreach (var tab in t.Tabs)
                {
                    AddAll(tab.Children);
                }

                break;
            case FieldNode f:
                if (string.IsNullOrEmpty(f.ValueId))
                {
                    throw new InvalidOperationException($"Field '{f.Label}' has no value id.");
                }

                if (Metadata(f.ValueId) is { } registered)
                {
                    if (!FieldSpecMetadata.Agree(registered.Spec, f.Spec))
                        throw new InvalidOperationException($"Registered and rendered specs conflict for field '{f.ValueId}'.");
                    _specs[f.ValueId] = registered.Spec;
                }
                else _specs[f.ValueId] = f.Spec;
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

                        Add(cell);
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
    {
        foreach (var n in nodes)
        {
            Add(n);
        }
    }
}
