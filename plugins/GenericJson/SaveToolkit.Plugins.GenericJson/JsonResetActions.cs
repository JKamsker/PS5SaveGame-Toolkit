using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonResetActions
{
    internal const string ActionId = "reset-imported";
    internal const int MaxChanges = 5000;
    public static ActionNode View() => new(ActionId, "Reset existing values to imported values")
    {
        SupportsPlanning = true,
        Description = "Preview original/current values, then reset as one undo step. Search selections can cover all matching rows. "
            + "New entries are skipped; removed entries and order require structural undo. This restores the imported file, not game defaults.",
    };

    public static ActionPlan Plan(JsonTokenTree tree, ActionScope scope)
    {
        if (scope.QueryScope is { CollectionId: not JsonSearch.CollectionId } || scope.QueryScope is not null && scope.RowKeys is null)
            return new() { Errors = [new(ActionId, "Use the JSON tree collection with a resolved selection.")] };
        var selected = scope.RowKeys?.ToHashSet(StringComparer.Ordinal);
        if (selected is not null && (selected.Count > MaxChanges || selected.Any(key => tree.FindValue(key) is null)))
            return new() { Errors = [new(ActionId, "Selection contains a missing JSON node.")] };
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var node in tree.Nodes)
        {
            if (selected is not null && !selected.Contains(node.ValueId)) continue;
            if (node.IsContainer || !node.Imported || node.Spec?.ReadOnly == true)
            {
                if (selected is not null) skipped.Add(new(node.ValueId, "Only existing editable imported scalar values can be reset; use undo for structure."));
                continue;
            }
            if (node.Current == node.Original) continue;
            changes.Add(new(node.ValueId, JsonScalar.ReviewTransition(JsonPaths.ForNode(node), node.Current, node.Original), node.Current, node.Original));
            if (changes.Count + skipped.Count > MaxChanges)
                return new() { Errors = [new(ActionId, "Reset affects too many fields; narrow the selection to at most 5,000.")] };
        }
        return new() { Changes = changes, SkippedRows = skipped };
    }
}
