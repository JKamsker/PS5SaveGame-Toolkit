using System.Collections.Immutable;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Editing;

/// <summary>Tracks declarations without rejecting older or incompletely loaded plugin views.</summary>
internal sealed class SelectionBindingIndex
{
    private ImmutableDictionary<string, ImmutableHashSet<ViewNode>> _tables =
        ImmutableDictionary.Create<string, ImmutableHashSet<ViewNode>>(StringComparer.Ordinal);
    private ImmutableHashSet<ActionNode> _actions = ImmutableHashSet.Create<ActionNode>(ReferenceEqualityComparer.Instance);

    internal SelectionBindingIndex Copy(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new SelectionBindingIndex { _tables = _tables, _actions = _actions };
    }

    public void Add(ViewNode node)
    {
        var id = node switch
        {
            TableNode table => table.SelectionId,
            QueryableTableNode table => table.CollectionId,
            _ => null,
        };
        if (id is not null)
        {
            if (!_tables.TryGetValue(id, out var tables))
                tables = ImmutableHashSet.Create<ViewNode>(ReferenceEqualityComparer.Instance);
            _tables = _tables.SetItem(id, tables.Add(node));
        }
        if (node is ActionNode { SelectionTableId: not null } action) _actions = _actions.Add(action);
    }

    public IReadOnlyList<string> Diagnostics() => _actions.SelectMany(action =>
    {
        var id = action.SelectionTableId!;
        if (string.IsNullOrWhiteSpace(id) || !_tables.TryGetValue(id, out var tables))
            return new[] { $"Action '{action.ActionId}' declares SelectionTableId '{id}', but no table with that selection ID was found in the indexed view (unloaded content may be incomplete)." };
        return tables.Count > 1
            ? [$"Action '{action.ActionId}' declares ambiguous SelectionTableId '{id}': multiple tables use that selection ID."]
            : Array.Empty<string>();
    }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public void Clear()
    {
        _tables = _tables.Clear();
        _actions = _actions.Clear();
    }
}
