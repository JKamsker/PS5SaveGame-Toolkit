using System.Text.Json;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonObjectActions
{
    private const int MaxNameLength = 16_384;

    public static IReadOnlyList<ActionNode> View(JsonValueToken value)
    {
        var count = value.Children.Count;
        var actions = new List<ActionNode>
        {
            Action("insert", "Insert object member", [JsonStructureActions.Index("at", "Insert at entry index", count, count), Name(), .. JsonTemplates.Parameters]),
        };
        if (count > 0)
        {
            actions.Add(Action("remove", "Remove object member occurrence", [JsonStructureActions.Index("at", "Entry index (occurrence order)", count - 1)]));
            actions.Add(Action("rename", "Rename object member occurrence", [JsonStructureActions.Index("at", "Entry index (occurrence order)", count - 1), Name()]));
        }
        return actions;

        ActionNode Action(string operation, string label, IReadOnlyList<FieldNode> parameters) => new($"object/{value.Index}/{operation}", label)
        {
            SupportsPlanning = true, Parameters = parameters, Description = JsonStructureActions.SafetyWarning
                + " Entry indices are zero-based; preview shows exact occurrence paths. New names must be unique.", Tone = Tone.Warning,
        };
    }

    public static void Mutate(JsonTokenTree tree, JsonValueToken parent, string operation, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var at = JsonStructureActions.ReadIndex(parameters, "at", operation == "insert" ? parent.Children.Count : parent.Children.Count - 1);
        switch (operation)
        {
            case "insert":
                var name = ReadName(parameters);
                RequireUnique(parent, name);
                // Parsing the whole member establishes its exact key/colon/value spans, including Unicode.
                var member = JsonTokenTree.Parse("{" + JsonSerializer.Serialize(name) + ":" + JsonTemplates.Read(parameters) + "}").Nodes[0].Children[0];
                JsonStructureActions.Insert(tree, parent, member, at);
                break;
            case "remove":
                parent.Children.RemoveAt(at);
                break;
            case "rename":
                var replacement = ReadName(parameters);
                RequireUnique(parent, replacement);
                parent.Children[at].Label = replacement;
                break;
            default: throw new ArgumentException("Unknown object operation.");
        }
    }

    private static FieldNode Name() => new("name", "Exact member name", new TextSpec { Multiline = true, MaxLength = MaxNameLength }, FieldValue.Of(""));

    private static string ReadName(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("name", out var name) || name.Kind != FieldValueKind.Text || name.AsString().Length > MaxNameLength
            || JsonScalar.Validate(FieldValue.Union(JsonScalar.Text, name)) is not null)
            throw new ArgumentException("Supply a valid Unicode member name of at most 16,384 characters.");
        return name.AsString();
    }

    private static void RequireUnique(JsonValueToken parent, string name)
    {
        if (parent.Children.Any(child => child.Label == name))
            throw new ArgumentException("The object already has that member name. Select an explicit occurrence for removal; new names must be unique.");
    }
}
