using System.Text.Json;
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
            actions.Add(Action("move-to", "Move subtree to array", [
                new("source", "Source subtree identity", new TextSpec(), FieldValue.Of(array.Children[0].ValueId)),
                new("destination", "Destination array identity", new TextSpec(), FieldValue.Of(array.ValueId)),
                JsonStructureActions.Index("at", "Final insertion index", JsonTokenTree.MaxNodes),
            ]) with
            {
                Description = JsonStructureActions.SafetyWarning
                    + " Use exact value/N identities from Search. The source must be a direct entry of this array; the destination must be an array in this document. The index is zero-based after removal.",
            });
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
            case "move-to":
                MoveTo(tree, array, parameters);
                break;
            default: throw new ArgumentException("Unknown array operation.");
        }
    }

    private static void MoveTo(JsonTokenTree tree, JsonValueToken sourceArray, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var moving = ReadIdentity("source");
        var destination = ReadIdentity("destination");
        if (moving.Parent != sourceArray)
            throw new ArgumentException("Source subtree must be a direct entry of the addressed source array.");
        if (destination.Kind != JsonTokenType.StartArray)
            throw new ArgumentException("Destination must be a JSON array.");
        for (var ancestor = destination; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor == moving)
                throw new ArgumentException("Cannot move a subtree into itself or its descendant.");

        var sameArray = sourceArray == destination;
        var at = JsonStructureActions.ReadIndex(parameters, "at", destination.Children.Count - (sameArray ? 1 : 0));
        if (sameArray && at == moving.Position)
            throw new ArgumentException("Choose a different final index.");

        sourceArray.Children.RemoveAt(moving.Position);
        // Only the entry boundary adopts destination trivia. All tokens inside the subtree,
        // including duplicate member names and their original escapes, retain their slices and IDs.
        if (!sameArray) moving.SeparatorBefore = destination.DefaultSeparator;
        destination.Children.Insert(at, moving);

        JsonValueToken ReadIdentity(string name)
        {
            if (!parameters.TryGetValue(name, out var value) || value.Kind != FieldValueKind.Text)
                throw new ArgumentException($"Supply the {name} session identity (value/N).");
            return tree.FindValue(value.AsString())
                ?? throw new ArgumentException($"The {name} identity is unknown or no longer exists.");
        }
    }
}
