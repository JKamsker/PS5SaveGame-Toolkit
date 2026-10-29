using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Testing;

/// <summary>Bounded discovery only; ownership always comes from the opted-in fixture.</summary>
internal sealed class SelectionScopeTraversal(ISaveDocument document, SelectionScopeLimits limits)
{
    private readonly ConformanceQueryPages _queries = new(document, limits.MaxQueryPages);
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _tables = new(StringComparer.Ordinal);
    private readonly List<(ActionNode Action, string? Table)> _actions = [];
    private int _nodes;
    public bool Complete { get; private set; } = true;

    public string? Check(SelectionScopeCase fixture)
    {
        Visit(document.BuildView(), 0, null);
        if (!Complete || _queries.BudgetCut) return "View traversal cut; selection evidence is incomplete.";
        if (!_tables.TryGetValue(fixture.SelectionTableId, out var rows)) return "Selection table was not found in the view.";
        if (fixture.RowKeys.Count == 0 || fixture.RowKeys.Any(key => !rows.Contains(key))) return "Selected rows are absent from the table.";
        if (fixture.RowOwnedValueIds.Keys.Any(key => !rows.Contains(key))) return "Declared ownership refers to an unknown row.";
        if (!_actions.Any(entry => entry.Action.ActionId == fixture.ActionId && entry.Action.SupportsPlanning &&
            (entry.Action.SelectionTableId ?? entry.Table) == fixture.SelectionTableId))
            return "No planned action is associated with the selection table.";
        return null;
    }

    private bool Count(int depth)
    {
        if (++_nodes <= limits.MaxNodes && depth <= limits.MaxDepth) return true;
        Complete = false;
        return false;
    }

    private void Visit(ViewNode node, int depth, string? tableId)
    {
        if (!Complete || !Count(depth)) return;
        switch (node)
        {
            case SectionNode { LazyId: { } id }:
                if (!_expanded.Add(id)) return;
                if (_expanded.Count > limits.MaxLazyExpansions) { Complete = false; return; }
                foreach (var child in document.ExpandNode(id)) Visit(child, depth + 1, tableId);
                break;
            case SectionNode section:
                foreach (var child in section.Children)
                {
                    Visit(child, depth + 1, tableId);
                    if (!Complete) return;
                }
                break;
            case TabsNode tabs:
                foreach (var tab in tabs.Tabs)
                    foreach (var child in tab.Children) Visit(child, depth + 1, tableId);
                break;
            case TableNode table:
                AddTable(table.SelectionId);
                VisitRows(table.Rows, table.SelectionId, depth);
                break;
            case QueryableTableNode table:
                AddTable(table.CollectionId);
                foreach (var page in _queries.Read(table)) VisitRows(page.Rows, table.CollectionId, depth);
                break;
            case ActionNode action:
                _actions.Add((action, tableId));
                break;
        }
    }

    private void AddTable(string? id)
    {
        if (id is not null && !_tables.TryAdd(id, new(StringComparer.Ordinal))) Complete = false;
    }

    private void VisitRows(IReadOnlyList<TableRow> rows, string? id, int depth)
    {
        foreach (var row in rows)
        {
            if (!Count(depth + 1)) return;
            if (id is not null && !_tables[id].Add(row.Key)) Complete = false;
            foreach (var cell in row.Cells.Values) Visit(cell, depth + 1, id);
            if (!Complete) return;
        }
    }
}
