using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Editing;

/// <summary>Tracks declarations without rejecting older or incompletely loaded plugin views.</summary>
internal sealed class SelectionBindingIndex
{
    private readonly Dictionary<string, HashSet<ViewNode>> _tables = new(StringComparer.Ordinal);
    private readonly HashSet<ActionNode> _actions = new(ReferenceEqualityComparer.Instance);

    internal SelectionBindingIndex Copy(CancellationToken cancellationToken)
    {
        var copy = new SelectionBindingIndex();
        foreach (var tables in _tables.Values)
            foreach (var table in tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                copy.Add(table);
            }
        foreach (var action in _actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            copy.Add(action);
        }
        return copy;
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
                _tables.Add(id, tables = new(ReferenceEqualityComparer.Instance));
            tables.Add(node);
        }
        if (node is ActionNode { SelectionTableId: not null } action) _actions.Add(action);
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
        _tables.Clear();
        _actions.Clear();
    }
}
