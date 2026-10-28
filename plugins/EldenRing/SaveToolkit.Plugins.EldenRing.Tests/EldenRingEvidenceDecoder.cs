using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.EldenRing.Tests;

// F1 hook deliberately omits names, account/profile settings, passwords and all raw blobs.
internal sealed class EldenRingEvidenceDecoder : ISaveDecoder
{
    public string Id => "eldenring-evidence";
    public bool CanDecode(ISaveFile file) => new EldenRingPlugin().Probe(file).Score > ProbeScore.Fallback;

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes) throw new InvalidDataException("Evidence decoder file budget exceeded.");
        var bytes = file.ReadAllBytes();
        var save = EldenRingParser.Parse(bytes);
        var tree = new SemanticTreeBuilder(Id, limits);
        tree.Add("profile/version", "integer", Number(save.ProfileVersion));
        foreach (var slot in save.Slots.Where(slot => slot.Active && slot.Version == 150))
        {
            var semantic = SemanticReader.Read(bytes, slot);
            var player = slot.PlayerOffset!.Value;
            tree.Add($"slots/{slot.Index}/held-runes", "integer", Number(EldenRingParser.ReadUInt32(bytes, player + 0x64)));
            foreach (var container in semantic.Containers)
            {
                var prefix = $"slots/{slot.Index}/{container.Id}";
                tree.Add(prefix + "/common-count", "integer", Number(container.CommonCount));
                tree.Add(prefix + "/key-count", "integer", Number(container.KeyCount));
                // Inventory index and handle, plus occurrence for genuine duplicates, align relocated rows.
                var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var entry in container.Entries)
                {
                    var identity = $"{prefix}/{entry.Group}[handle={entry.Handle:X8},index={entry.InventoryIndex}]";
                    var occurrence = occurrences.GetValueOrDefault(identity);
                    occurrences[identity] = occurrence + 1;
                    var path = occurrence == 0 ? identity : identity + $"[occurrence={occurrence + 1}]";
                    if (!tree.Add(path + "/quantity", "integer", Number(entry.Quantity))) return tree.Build();
                    if (!tree.Add(path + "/identity", "text", entry.RawId)) return tree.Build();
                }
            }
        }
        return tree.Build();
    }

    private static string Number(uint value) => value.ToString(CultureInfo.InvariantCulture);
}
