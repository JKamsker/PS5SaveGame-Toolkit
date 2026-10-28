using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonArrayActions
{
    private const int MaxInsertedText = 1024 * 1024;
    internal const string SafetyWarning = "Advanced JSON syntax edit. Array membership/order may have game-specific dependencies; console acceptance is unknown.";

    public static string StructureId(JsonValueToken node) => $"structure/{node.Index}";
    public static FieldValue StructureValue(JsonValueToken node)
    {
        const int DisplayedIds = 64;
        var ids = string.Join(", ", node.Children.Select(n => n.ValueId));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ids)));
        return FieldValue.Of($"{(node.Kind == JsonTokenType.StartArray ? "array" : "object")} ({node.Children.Count} entries): "
            + string.Join(", ", node.Children.Take(DisplayedIds).Select(n => n.ValueId))
            + (node.Children.Count > DisplayedIds ? ", …" : "") + $"; order SHA-256 {digest}");
    }

    public static IReadOnlyList<ActionNode> View(JsonValueToken array)
    {
        var count = array.Children.Count;
        var actions = new List<ActionNode>
        {
            Action("insert", "Insert JSON value", [Index("at", "Insert at index", count, count),
                new("json", "JSON value (explicit template)", new TextSpec { Multiline = true, MaxLength = MaxInsertedText }, FieldValue.Of("null"))]),
        };
        if (count > 0)
        {
            actions.Add(Action("remove", "Remove array entry", [Index("at", "Remove index", count - 1)]));
            actions.Add(Action("move", "Move array entry", [Index("from", "Source index", count - 1), Index("to", "Final index", count - 1)]));
            actions.Add(Action("duplicate", "Duplicate array entry", [Index("from", "Source index", count - 1), Index("at", "Insert at index", count, count)]));
        }
        return actions;

        ActionNode Action(string operation, string label, IReadOnlyList<FieldNode> parameters) =>
            new($"array/{array.Index}/{operation}", label)
            {
                SupportsPlanning = true, Parameters = parameters, Description = SafetyWarning, Tone = Tone.Warning,
            };
    }

    public static ActionPlan Plan(JsonTokenTree tree, string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        try
        {
            if (scope.RowKeys is not null || scope.QueryScope is not null)
                throw new ArgumentException("Array actions use the explicitly addressed array/index, not a row selection.");
            var trial = tree.Clone();
            Mutate(trial, actionId, parameters);
            _ = JsonTreeWriter.Write(trial);
            var before = tree.Nodes.ToDictionary(n => n.Index);
            var after = trial.Nodes.ToDictionary(n => n.Index);
            var changes = new List<PlannedChange>();
            foreach (var id in before.Keys.Union(after.Keys))
            {
                var old = before.GetValueOrDefault(id);
                var next = after.GetValueOrDefault(id);
                var node = next ?? old!;
                var oldValue = old is null ? FieldValue.Null : ReviewValue(old);
                var newValue = next is null ? FieldValue.Null : ReviewValue(next);
                if (old is not null && next is not null && oldValue == newValue) continue;
                var label = $"{node.Label} ({node.ValueId})";
                if (!node.IsContainer) label = JsonScalar.ReviewTransition(label, oldValue, newValue, old is not null, next is not null);
                changes.Add(new(node.IsContainer ? StructureId(node) : node.ValueId, label, oldValue, newValue)
                {
                    OldExists = old is not null, NewExists = next is not null,
                });
                if (changes.Count > JsonResetActions.MaxChanges) throw new ArgumentException("Operation affects more than 5,000 review fields; narrow the edit.");
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
        if (parts.Length != 3 || parts[0] != "array" || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException("Unknown array action.");
        var array = tree.Nodes.FirstOrDefault(n => n.Index == id && n.Kind == JsonTokenType.StartArray)
            ?? throw new ArgumentException("Array no longer exists.");
        var count = array.Children.Count;
        switch (parts[2])
        {
            case "insert":
                var at = ReadIndex(parameters, "at", count);
                if (!parameters.TryGetValue("json", out var json) || json.Kind != FieldValueKind.Text || json.AsString().Length > MaxInsertedText)
                    throw new ArgumentException("Supply a JSON template of at most 1 MiB of text.");
                Insert(JsonTokenTree.Parse(json.AsString()).Nodes[0], at);
                break;
            case "remove":
                array.Children.RemoveAt(ReadIndex(parameters, "at", count - 1));
                break;
            case "move":
                var from = ReadIndex(parameters, "from", count - 1);
                var to = ReadIndex(parameters, "to", count - 1);
                if (from == to) throw new ArgumentException("Choose a different final index.");
                var moving = array.Children[from];
                array.Children.RemoveAt(from);
                array.Children.Insert(to, moving);
                break;
            case "duplicate":
                var source = array.Children[ReadIndex(parameters, "from", count - 1)];
                var destination = ReadIndex(parameters, "at", count);
                Insert(JsonTokenTree.Parse(JsonTreeWriter.WriteValue(source)).Nodes[0], destination);
                break;
            default: throw new ArgumentException("Unknown array operation.");
        }
        tree.Reindex();

        void Insert(JsonValueToken node, int index)
        {
            if (tree.Nodes.Count + Count(node) > JsonTokenTree.MaxNodes)
                throw new SaveFormatException("Insertion exceeds the 250,000 value safety limit.");
            AssignIds(node);
            array.Children.Insert(index, node);
        }

        static int Count(JsonValueToken node) => 1 + node.Children.Sum(Count);
        void AssignIds(JsonValueToken node)
        {
            node.Index = checked(tree.NextId++);
            node.Imported = false;
            foreach (var child in node.Children) AssignIds(child);
        }
    }

    private static FieldValue ReviewValue(JsonValueToken node) => node.IsContainer ? StructureValue(node) : node.Current;

    private static FieldNode Index(string key, string label, int maximum, int initial = 0) =>
        new(key, label, new IntegerSpec(0, maximum), FieldValue.Of((long)initial));

    private static int ReadIndex(IReadOnlyDictionary<string, FieldValue> parameters, string name, int maximum)
    {
        if (!parameters.TryGetValue(name, out var value) || value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximum)
            throw new ArgumentException("Array index is outside the current bounds.");
        return value.AsInt32();
    }
}
