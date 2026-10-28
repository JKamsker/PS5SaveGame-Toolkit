using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonInspection
{
    internal const string ActionId = "inspect-json";

    public static ActionNode View() => new(ActionId, "Validate JSON and summarize structure")
    {
        Mutates = false,
        Description = "Reparse the current output and report aggregate syntax counts. Does not establish game compatibility or display stored values.",
    };

    public static ActionOutcome Run(JsonTokenTree tree)
    {
        var bytes = JsonTreeWriter.Write(tree);
        var containers = tree.Nodes.Count(n => n.IsContainer);
        return new()
        {
            Message = $"Valid UTF-8 JSON: {bytes.Length} bytes, {tree.Nodes.Count} values, {containers} containers, {tree.Nodes.Count - containers} scalars. Exact token round-trip validation passed.",
            Warnings = ["JSON syntax validity does not establish game compatibility, external integrity or console acceptance."],
        };
    }
}
