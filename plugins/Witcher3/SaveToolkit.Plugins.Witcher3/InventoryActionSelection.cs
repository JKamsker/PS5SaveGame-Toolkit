using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3;

internal static class InventoryActionSelection
{
    public static (IReadOnlyList<InventoryItem> Items, IReadOnlyList<FieldError> Errors) Resolve(string actionId,
        IReadOnlyList<InventoryItem> items, bool canEdit, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var errors = new List<FieldError>();
        if (!canEdit) { errors.Add(new(actionId, "Unsupported player schema; this document is read-only.")); }
        if (parameters.Count != 0) { errors.Add(new(actionId, "This action has no parameters.")); }
        if (scope.QueryScope is { } query && (query.CollectionId != InventoryViews.CollectionId || scope.RowKeys is null))
        {
            errors.Add(new(actionId, "A query scope must resolve to player-inventory row keys before planning."));
        }
        var byKey = items.ToDictionary(i => Key(i), StringComparer.Ordinal);
        var keys = scope.RowKeys ?? items.Select(Key).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || keys.Any(k => !byKey.ContainsKey(k)))
        {
            errors.Add(new(actionId, "Selection contains duplicate or unknown inventory instances."));
        }
        return (errors.Count > 0 ? [] : keys.Select(k => byKey[k]).ToArray(), errors);
    }

    public static string Key(InventoryItem item) => item.Id.ToString(CultureInfo.InvariantCulture);
}
