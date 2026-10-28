using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record SavedMapTag(string Id, bool Known, bool Discovered, bool Disabled);

/// <summary>Exact counted native map-tag blocks. No coordinates, quest or reward semantics are inferred.</summary>
internal static class MapReader
{
    private const int MaximumTags = 10_000;
    private static readonly string[] Blocks = ["KnownMapPinTags", "DiscoveredMapPinTags", "DisabledMapPinTags"];

    public static IReadOnlyList<SavedMapTag>? Read(Sav3Tree tree, byte[] data, IReadOnlyList<string> names)
    {
        var managers = tree.Nodes.Where(n => n.Tag == "BS" && n.Name == "CCommonMapManager").ToArray();
        if (managers.Length == 0) { return null; }
        if (managers.Length != 1) { throw new SaveFormatException("Ambiguous common map manager."); }
        return ReadTags(managers[0], tree.Nodes, data, names);
    }

    internal static IReadOnlyList<SavedMapTag> ReadTags(Sav3Node manager, IReadOnlyList<Sav3Node> nodes,
        byte[] data, IReadOnlyList<string> names)
    {
        var sets = new List<HashSet<string>>();
        foreach (var blockName in Blocks)
        {
            var blocks = manager.Children.Select(i => nodes[i]).Where(n => n.Tag == "BS" && n.Name == blockName).ToArray();
            if (blocks.Length != 1) { throw new SaveFormatException($"Missing or ambiguous {blockName} block."); }
            var children = blocks[0].Children.Select(i => nodes[i]).ToArray();
            if (children.Length == 0 || children[0] is not { Tag: "VL", Name: "Size", Type: "Uint32", ValueSize: sizeof(int) })
            {
                throw new SaveFormatException($"Invalid {blockName} count field.");
            }
            var count = BinaryData.Int32(data, children[0].ValueOffset);
            if (count is < 0 or > MaximumTags || count != children.Length - 1)
            {
                throw new SaveFormatException($"Invalid {blockName} count.");
            }
            var tags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in children.Skip(1))
            {
                if (node is not { Tag: "VL", Name: "MapPinTag", Type: "CName", ValueSize: sizeof(ushort) })
                {
                    throw new SaveFormatException($"Unsupported {blockName} tag record.");
                }
                var index = BinaryData.UInt16(data, node.ValueOffset);
                if (index < 1 || index > names.Count || !tags.Add(names[index - 1]))
                {
                    throw new SaveFormatException($"Invalid or duplicate {blockName} identity.");
                }
            }
            sets.Add(tags);
        }
        return sets.SelectMany(s => s).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => new SavedMapTag(id, sets[0].Contains(id), sets[1].Contains(id), sets[2].Contains(id))).ToArray();
    }
}
