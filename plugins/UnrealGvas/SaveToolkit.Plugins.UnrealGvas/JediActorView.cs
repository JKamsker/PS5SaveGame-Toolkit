using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

internal static class JediActorView
{
    private const int SummaryLimit = 100;

    public static IReadOnlyList<ViewNode> BuildSummary(IReadOnlyList<GvasNode> roots)
    {
        var entries = GvasPropertyEntry.Create(roots, "actors", SummaryLimit);
        var rows = entries.Select(entry => new TableRow(entry.Id, new Dictionary<string, ViewNode>
        {
            ["path"] = new TextNode(entry.Path) { Monospace = true },
            ["type"] = new TextNode(entry.Node.Type),
            ["value"] = new TextNode(GvasPropertyEntry.Clip(entry.Value, 256)),
        })).ToArray();
        return [
            new TextNode("Storage summary only; custom actor schemas and gameplay roles may be incomplete. Use the Actors tab for server-backed paging/search/filter/sort over the complete bounded index.") { Tone = Tone.Warning },
            new TableNode([new("path", "Path"), new("type", "Type"), new("value", "Value")], rows)
            { Title = "Actor property summary", Description = $"First {SummaryLimit} of {CountNodes(roots)} indexed nodes; full collection is available through F8 in Actors." },
        ];
    }

    private static int CountNodes(IReadOnlyList<GvasNode> nodes)
    {
        int count = nodes.Count;
        for (int i = 0; i < nodes.Count; i++) count += CountNodes(nodes[i].Children);
        return count;
    }
}
