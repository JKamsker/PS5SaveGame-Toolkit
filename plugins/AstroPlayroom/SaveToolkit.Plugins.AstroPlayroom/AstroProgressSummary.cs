using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal static class AstroProgressSummary
{
    public static SectionNode Build(IReadOnlyList<AstroField> fields, AstroReferences references,
        Func<AstroField, FieldValue> current)
    {
        var world = fields.Where(f => f.Group == "World" && f.Label == "IsCleared").ToArray();
        var visited = fields.Count(f => f.Group == "World" && f.Label == "IsVisited");
        var visitedTrue = fields.Count(f => f.Group == "World" && f.Label == "IsVisited" && current(f).AsBool());
        var progress = fields.Where(f => f.Group == "Progress" && f.Spec is BoolSpec).ToArray();
        var progressTrue = progress.Count(f => current(f).AsBool());
        var clearedTrue = world.Count(f => current(f).AsBool());

        return new SectionNode("Stored progress summary", [
            new TextNode("These counts summarize saved booleans. They are not a gameplay completion proof: notification, reward, trophy and time-attack dependencies remain unverified.") { Tone = Tone.Warning },
            new StatsNode([
                new Stat("Main clear-like flags", $"{clearedTrue}/{world.Length}"),
                new Stat("Visited stage flags", $"{visitedTrue}/{visited}"),
                new Stat("Progress flags true", $"{progressTrue}/{progress.Length}")]),
            new TableNode([
                new TableColumn("scope", "Saved scope") { Width = ColumnWidth.Wide },
                new TableColumn("state", "Stored state"),
                new TableColumn("confidence", "Meaning") { Width = ColumnWidth.Narrow }
            ], world.Select(field => WorldRow(field, references, current)).Concat(
                progress.Select(field => new TableRow(field.Token.Id, new Dictionary<string, ViewNode>(StringComparer.Ordinal)
                {
                    ["scope"] = new TextNode(field.Path["PreInstall/".Length..]) { Monospace = true },
                    ["state"] = new TextNode(current(field).AsBool() ? "true" : "false"),
                    ["confidence"] = new TextNode("Inferred")
                }))).ToArray())
            { Title = "Saved progress flags", Description = "Raw paths and current values are retained so duplicate or internal/test scopes are not collapsed." }
        ]) { Description = "Derived read-only totals over existing fields; no records are created or removed." };
    }

    private static TableRow WorldRow(AstroField field, AstroReferences references, Func<AstroField, FieldValue> current)
    {
        var path = field.Path["PreInstall/".Length..];
        var parent = path[..path.LastIndexOf('/')];
        var reference = references.World(parent);
        var confidence = reference is null ? "Unknown" : "Inferred";
        var label = reference is null ? parent : reference.Name;
        return new TableRow(field.Token.Id, new Dictionary<string, ViewNode>(StringComparer.Ordinal)
        {
            ["scope"] = new TextNode(label) { Monospace = reference is null },
            ["state"] = new TextNode(current(field).AsBool() ? "clear flag true" : "clear flag false"),
            ["confidence"] = new TextNode(confidence)
        });
    }
}
