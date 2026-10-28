using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record GwentCard(int Index, int Copies)
{
    public bool Leader { get; init; }
    public int CopiesOffset { get; init; }
}

internal static class GwentReader
{
    private const int MaximumCards = 4096;

    public static IReadOnlyList<GwentCard>? Read(Sav3Tree tree, byte[] data)
    {
        var managers = tree.Nodes.Where(n => n.Tag == "BS" && n.Name == "CR4GwintManager").ToArray();
        if (managers.Length == 0) { return null; }
        if (managers.Length != 1) { throw new SaveFormatException("Ambiguous Gwent manager."); }
        return ReadCollection(managers[0], tree.Nodes, data);
    }

    internal static IReadOnlyList<GwentCard> ReadCollection(Sav3Node manager, IReadOnlyList<Sav3Node> nodes, byte[] data)
    {
        var children = manager.Children.Select(id => nodes[id]).ToArray();
        var boundaries = children.Where(n => n.Name == "SBSelectedDeckIndex" && n.Tag == "BS").ToArray();
        if (boundaries.Length != 1) { throw new SaveFormatException("Missing or ambiguous Gwent collection/deck boundary."); }
        var cards = new List<GwentCard>();
        var ids = new HashSet<int>();
        ReadGroup("SBCollectionCardSize", "SBCollectionCard", false);
        ReadGroup("SBLeaderCollectionCardSize", "SBLeaderCollectionCard", true);
        return cards;

        void ReadGroup(string groupName, string recordName, bool leader)
        {
            var groups = children.Where(n => n.Tag == "BS" && n.Name == groupName && n.Offset < boundaries[0].Offset).ToArray();
            if (groups.Length != 1) { throw new SaveFormatException($"Missing or ambiguous Gwent {groupName}."); }
            var entries = groups[0].Children.Select(id => nodes[id]).ToArray();
            var sizes = entries.Where(n => n.Name == "Size").ToArray();
            if (sizes.Length != 1 || sizes[0].Tag != "VL" || sizes[0].Type != "Uint32" || sizes[0].ValueSize != sizeof(uint))
            {
                throw new SaveFormatException("Invalid Gwent declared collection size.");
            }

            var count = BinaryData.Int32(data, sizes[0].ValueOffset);
            var records = entries.Where(n => n.Tag == "BS" && n.Name == recordName).ToArray();
            if (count < 0 || count > MaximumCards - cards.Count || records.Length != count || entries.Length != count + 1)
            {
                throw new SaveFormatException("Gwent collection count disagrees with its records.");
            }

            foreach (var record in records)
            {
                var values = record.Children.Select(id => nodes[id]).ToArray();
                var indices = values.Where(n => n.Name == "cardIndex").ToArray();
                var copies = values.Where(n => n.Name == "numCopies").ToArray();
                if (values.Length != 2 || indices.Length != 1 || copies.Length != 1 ||
                    values.Any(n => n.Tag != "VL" || n.Type != "Int32" || n.ValueSize != sizeof(int)))
                {
                    throw new SaveFormatException("Invalid Gwent collection pair type or boundary.");
                }

                var index = BinaryData.Int32(data, indices[0].ValueOffset);
                var quantity = BinaryData.Int32(data, copies[0].ValueOffset);
                if (index < 0 || quantity < 0 || !ids.Add(index)) { throw new SaveFormatException("Invalid Gwent card count or identity."); }
                cards.Add(new GwentCard(index, quantity) { Leader = leader, CopiesOffset = copies[0].ValueOffset });
            }
        }
    }
}
