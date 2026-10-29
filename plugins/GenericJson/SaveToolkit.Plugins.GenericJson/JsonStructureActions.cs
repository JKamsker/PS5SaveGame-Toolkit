using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonStructureActions
{
    internal const string SafetyWarning = JsonWriteWarnings.Raw
        + " Advanced JSON syntax edit. Membership, names and order may have game-specific dependencies; console acceptance is unknown. Templates describe JSON types, not a validated game schema.";
    private const int DisplayedIds = 64;

    public static string StructureId(JsonValueToken node) => $"structure/{node.Index}";

    public static FieldValue StructureValue(JsonValueToken node)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var displayed = new List<string>();
        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            var entry = node.Kind == JsonTokenType.StartObject ? $"{child.ValueId}:{JsonSerializer.Serialize(child.Label)}" : child.ValueId;
            if (i > 0) hash.AppendData(", "u8);
            hash.AppendData(Encoding.UTF8.GetBytes(entry));
            if (i < DisplayedIds) displayed.Add(entry.Length <= 160 ? entry : entry[..160] + "…");
        }
        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        // Bound the displayed names as well as their count. The digest covers every complete name.
        var prefix = string.Join(", ", displayed);
        return FieldValue.Of($"{(node.Kind == JsonTokenType.StartArray ? "array" : "object")} ({node.Children.Count} entries): "
            + prefix + (node.Children.Count > DisplayedIds ? ", …" : "") + $"; order/name SHA-256 {digest}");
    }

    public static ActionPlan Plan(JsonTokenTree tree, string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        try
        {
            if (scope.RowKeys is not null || scope.QueryScope is not null)
                throw new ArgumentException("Structure actions use the explicitly addressed container/index, not a row selection.");
            var trial = tree.Clone();
            Mutate(trial, actionId, parameters);
            _ = JsonTreeWriter.Write(trial);
            var before = tree.ById;
            var after = trial.ById;
            var changes = new List<PlannedChange>();
            foreach (var id in before.Keys.Union(after.Keys))
            {
                var old = before.GetValueOrDefault(id);
                var next = after.GetValueOrDefault(id);
                var node = next ?? old!;
                var oldValue = old is null ? FieldValue.Null : ReviewValue(old);
                var newValue = next is null ? FieldValue.Null : ReviewValue(next);
                if (old is not null && next is not null && oldValue == newValue) continue;
                var label = $"{JsonPaths.ForNode(node)} ({node.ValueId})";
                if (!node.IsContainer) label = JsonScalar.ReviewTransition(label, oldValue, newValue, old is not null, next is not null);
                changes.Add(new(node.IsContainer ? StructureId(node) : node.ValueId, label, oldValue, newValue)
                {
                    OldExists = old is not null, NewExists = next is not null,
                });
                if (changes.Count > JsonResetActions.MaxChanges)
                    throw new ArgumentException("Operation affects more than 5,000 review fields; narrow the edit.");
            }
            return new() { Changes = changes, Warnings = [SafetyWarning] };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or SaveFormatException or OverflowException)
        {
            return new() { Errors = [new(actionId, ex.Message)] };
        }
    }

    public static void Mutate(JsonTokenTree tree, string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var parts = actionId.Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException("Unknown JSON structure action.");
        var container = tree.ById.GetValueOrDefault(id) ?? throw new ArgumentException("Container no longer exists.");
        if (parts[0] == "array" && container.Kind == JsonTokenType.StartArray)
            JsonArrayActions.Mutate(tree, container, parts[2], parameters);
        else if (parts[0] == "object" && container.Kind == JsonTokenType.StartObject)
            JsonObjectActions.Mutate(tree, container, parts[2], parameters);
        else throw new ArgumentException("Action does not address the expected JSON container type.");
        tree.Reindex();
    }

    public static void Insert(JsonTokenTree tree, JsonValueToken parent, JsonValueToken node, int index)
    {
        if (tree.Nodes.Count + Count(node) > JsonTokenTree.MaxNodes)
            throw new SaveFormatException("Insertion exceeds the 250,000 value safety limit.");
        AssignIds(node);
        parent.Children.Insert(index, node);

        static int Count(JsonValueToken value) => 1 + value.Children.Sum(Count);
        void AssignIds(JsonValueToken value)
        {
            value.Index = checked(tree.NextId++);
            value.Imported = false;
            foreach (var child in value.Children) AssignIds(child);
        }
    }

    public static FieldNode Index(string key, string label, int maximum, int initial = 0) =>
        new(key, label, new IntegerSpec(0, maximum), FieldValue.Of((long)initial));

    public static int ReadIndex(IReadOnlyDictionary<string, FieldValue> parameters, string name, int maximum)
    {
        if (!parameters.TryGetValue(name, out var value) || value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximum)
            throw new ArgumentException("Entry index is outside the current bounds.");
        return value.AsInt32();
    }

    private static FieldValue ReviewValue(JsonValueToken node) => node.IsContainer ? StructureValue(node) : node.Current;
}
