using System.Globalization;
using SaveToolkit.Plugins.UnrealGvas.Gvas;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Ordinal identity is independent of display names, duplicate tags and scalar length changes.</summary>
internal sealed record GvasPropertyEntry(string Id, string Path, GvasNode Node, IReadOnlyList<string> Ancestors)
{
    public string? EvidenceOverride { get; init; }
    public string Value => Node.Scalar?.Value is IFormattable number
        ? number.ToString(null, CultureInfo.InvariantCulture)
        : Node.Scalar?.Value.ToString() ?? Node.Note ?? $"{Node.Children.Count} children; {Node.Length} bytes";

    public string Evidence => EvidenceOverride ?? (Node.Type == "ObservedFString" ? "Unknown schema; observed string"
        : Node.Scalar is null && Node.Children.Count == 0 ? "Opaque / empty" : "Verified storage; game effects Unknown");

    public static IReadOnlyList<GvasPropertyEntry> Create(IReadOnlyList<GvasNode> roots, string prefix)
    {
        var result = new List<GvasPropertyEntry>();
        void Visit(IReadOnlyList<GvasNode> nodes, string parentId, string path, IReadOnlyList<string> ancestors)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var id = $"{parentId}/{i}";
                var label = Clip(path + "/" + Clip(node.Name, 128), 2048);
                result.Add(new(id, label, node, ancestors));
                Visit(node.Children, id, label, [.. ancestors, node.Name]);
            }
        }
        Visit(roots, prefix, "", []);
        return result;
    }

    public static string Clip(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
}
