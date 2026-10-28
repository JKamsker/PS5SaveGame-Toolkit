using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record JournalLink(string Resource, Guid Guid, byte Flags);
internal sealed record SavedJournalEntry(string Key, IReadOnlyList<JournalLink> Path, string Status, bool Unread);

/// <summary>Journal paths identify saved entries; no resource-to-quest-title or completion rules are invented.</summary>
internal static class JournalReader
{
    private const int MaximumEntries = 20_000;
    private const int MaximumPathLinks = 128;

    public static IReadOnlyList<SavedJournalEntry>? Read(Sav3Tree tree, byte[] data, IReadOnlyList<string> names)
    {
        var managers = tree.Roots.Select(id => tree.Nodes[id]).Where(n => n.Tag == "BS" && n.Name == "CJournalManager").ToArray();
        if (managers.Length == 0) { return null; }
        if (managers.Length != 1) { throw new SaveFormatException("Ambiguous journal manager."); }
        var groups = Children(managers[0]).Where(n => n.Tag == "BS" && n.Name == "JActiveEntries").ToArray();
        if (groups.Length != 1) { throw new SaveFormatException("Missing or ambiguous journal entries."); }
        var sizes = Children(groups[0]).Where(n => n.Name == "Size").ToArray();
        var entries = Children(groups[0]).Where(n => n.Tag == "BS" && n.Name == "SJournalEntryStatus").ToArray();
        if (sizes.Length != 1 || sizes[0].Tag != "VL" || sizes[0].Type != "Uint32" || sizes[0].ValueSize != sizeof(uint) ||
            entries.Length > MaximumEntries || entries.Length != BinaryData.Int32(data, sizes[0].ValueOffset) || groups[0].Children.Count != entries.Length + 1)
        {
            throw new SaveFormatException("Journal count disagrees with its records.");
        }

        var result = new List<SavedJournalEntry>(entries.Length);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var path = Only(entry, "path", "BS");
            var links = Children(path).ToArray();
            if (links.Length == 0 || links.Length % 3 != 0 || links.Length / 3 > MaximumPathLinks)
            {
                throw new SaveFormatException("Unsupported journal path shape.");
            }

            var chain = new List<JournalLink>();
            for (var i = 0; i < links.Length; i += 3)
            {
                if (links[i] is not { Tag: "VL", Name: "guid", Type: "CGUID", ValueSize: 16 } ||
                    links[i + 1] is not { Tag: "VL", Name: "resource", Type: "String" } ||
                    links[i + 2] is not { Tag: "VL", Name: "flags", Type: "Uint8", ValueSize: 1 })
                {
                    throw new SaveFormatException("Invalid journal GUID/resource/flags sequence.");
                }

                var raw = BinaryData.Slice(data, links[i + 1].ValueOffset, links[i + 1].ValueSize);
                var position = 0;
                var resource = SavedString.Read(raw, ref position);
                // Empty resources occur on GUID-only descendants; preserve the entire chain.
                if (position != raw.Length || resource.Any(char.IsControl)) { throw new SaveFormatException("Invalid journal resource string."); }
                chain.Add(new JournalLink(resource, new Guid(BinaryData.Slice(data, links[i].ValueOffset, 16)), data[links[i + 2].ValueOffset]));
            }

            var status = Only(entry, "status", "VL");
            var unread = Only(entry, "JEntryUnread", "VL");
            if (status.Type != "EJournalStatus" || status.ValueSize != sizeof(ushort) || unread.Type != "Bool" || unread.ValueSize != 1 || data[unread.ValueOffset] > 1)
            {
                throw new SaveFormatException("Invalid journal status or unread field.");
            }

            var statusIndex = BinaryData.UInt16(data, status.ValueOffset);
            if (statusIndex < 1 || statusIndex > names.Count) { throw new SaveFormatException("Invalid journal status name index."); }
            var identity = string.Join("\n", chain.Select(link => $"{link.Resource}|{link.Guid:D}"));
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            if (!identities.Add(key)) { throw new SaveFormatException("Ambiguous saved journal path identity."); }
            result.Add(new SavedJournalEntry(key, chain, names[statusIndex - 1], data[unread.ValueOffset] != 0));
        }

        return result;

        IEnumerable<Sav3Node> Children(Sav3Node node) => node.Children.Select(id => tree.Nodes[id]);
        Sav3Node Only(Sav3Node node, string name, string tag)
        {
            var matches = Children(node).Where(n => n.Name == name && n.Tag == tag).ToArray();
            return matches.Length == 1 ? matches[0] : throw new SaveFormatException($"Missing or ambiguous journal {name}.");
        }
    }
}
