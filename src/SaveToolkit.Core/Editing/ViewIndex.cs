using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Editing;

/// <summary>
/// Remembers every field spec and action the plugin has shown, so the framework can validate edits
/// and action calls without trusting the browser about types or ranges.
/// </summary>
public sealed class ViewIndex
{
    private readonly Dictionary<string, FieldSpec> _specs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActionNode> _actions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lazyIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _blobIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _assetIds = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, FieldSpec> Specs => _specs;

    public FieldSpec? Spec(string valueId) => _specs.GetValueOrDefault(valueId);

    public ActionNode? Action(string actionId) => _actions.GetValueOrDefault(actionId);

    public bool KnowsLazy(string lazyId) => _lazyIds.Contains(lazyId);

    public bool KnowsBlob(string blobId) => _blobIds.Contains(blobId);

    public bool KnowsAsset(string assetId) => _assetIds.Contains(assetId);

    /// <summary>Every value, action and lazy id currently indexed (the ids clients send back).</summary>
    public IEnumerable<string> BindingIds => _specs.Keys.Concat(_actions.Keys).Concat(_lazyIds);

    public bool KnowsBinding(string id) => _specs.ContainsKey(id) || _actions.ContainsKey(id) || _lazyIds.Contains(id);

    public void Clear()
    {
        _specs.Clear();
        _actions.Clear();
        _lazyIds.Clear();
        _blobIds.Clear();
        _assetIds.Clear();
    }

    /// <summary>Indexes a node tree. Throws <see cref="InvalidOperationException"/> on malformed views.</summary>
    public void Add(ViewNode node)
    {
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

                _specs[f.ValueId] = f.Spec;
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
