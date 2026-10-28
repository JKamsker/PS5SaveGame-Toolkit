using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonArrayActions
{
    public static IReadOnlyList<ActionNode> View(JsonValueToken array)
    {
        var count = array.Children.Count;
        var actions = new List<ActionNode>
        {
            Action("insert", "Insert JSON value", [JsonStructureActions.Index("at", "Insert at index", count, count), .. JsonTemplates.Parameters]),
        };
        if (count > 0)
        {
            actions.Add(Action("remove", "Remove array entry", [JsonStructureActions.Index("at", "Remove index", count - 1)]));
            actions.Add(Action("move", "Move array entry", [JsonStructureActions.Index("from", "Source index", count - 1), JsonStructureActions.Index("to", "Final index", count - 1)]));
            actions.Add(Action("duplicate", "Duplicate array entry", [JsonStructureActions.Index("from", "Source index", count - 1), JsonStructureActions.Index("at", "Insert at index", count, count)]));
        }
        return actions;

        ActionNode Action(string operation, string label, IReadOnlyList<FieldNode> parameters) =>
            new($"array/{array.Index}/{operation}", label)
            {
                SupportsPlanning = true, Parameters = parameters, Description = JsonStructureActions.SafetyWarning, Tone = Tone.Warning,
            };
    }

    public static void Mutate(JsonTokenTree tree, JsonValueToken array, string operation, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var count = array.Children.Count;
        switch (operation)
        {
            case "insert":
                var at = JsonStructureActions.ReadIndex(parameters, "at", count);
                JsonStructureActions.Insert(tree, array, JsonTokenTree.Parse(JsonTemplates.Read(parameters)).Nodes[0], at);
                break;
            case "remove":
                array.Children.RemoveAt(JsonStructureActions.ReadIndex(parameters, "at", count - 1));
                break;
            case "move":
                var from = JsonStructureActions.ReadIndex(parameters, "from", count - 1);
                var to = JsonStructureActions.ReadIndex(parameters, "to", count - 1);
                if (from == to) throw new ArgumentException("Choose a different final index.");
                var moving = array.Children[from];
                array.Children.RemoveAt(from);
                array.Children.Insert(to, moving);
                break;
            case "duplicate":
                var source = array.Children[JsonStructureActions.ReadIndex(parameters, "from", count - 1)];
                var destination = JsonStructureActions.ReadIndex(parameters, "at", count);
                JsonStructureActions.Insert(tree, array, JsonTokenTree.Parse(JsonTreeWriter.WriteValue(source)).Nodes[0], destination);
                break;
            default: throw new ArgumentException("Unknown array operation.");
        }
    }
}
