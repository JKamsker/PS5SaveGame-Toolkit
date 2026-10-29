using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.EldenRing;

// F1 hook deliberately omits names, account/profile settings, passwords and all raw blobs.
internal sealed class EldenRingEvidenceDecoder : ISaveDecoderHook
{
    public string Id => "eldenring-evidence";
    public bool CanDecode(ISaveFile file) => new EldenRingPlugin().Probe(file).Score > ProbeScore.Fallback;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes) throw new InvalidDataException("Evidence decoder file budget exceeded.");
        var bytes = file.ReadAllBytes();
        var save = EldenRingParser.Parse(bytes);
        // Output budgets must not turn malformed input into apparently valid partial evidence.
        var characters = save.Slots.Where(slot => slot.Active && slot.Version == EldenRingLayout.WritableSlotVersion)
            .Select(slot => (Slot: slot, Semantic: SemanticReader.Read(bytes, slot))).ToArray();
        if (!sink.Add("profile/version", "integer", Number(save.ProfileVersion))) return;
        foreach (var (slot, semantic) in characters)
        {
            var player = slot.PlayerOffset!.Value;
            if (!sink.Add($"slots/{slot.Index}/held-runes", "integer",
                Number(EldenRingParser.ReadUInt32(bytes, player + EldenRingLayout.RunesOffset)))) return;
            foreach (var container in semantic.Containers)
            {
                var prefix = $"slots/{slot.Index}/{container.Id}";
                if (!sink.Add(prefix + "/common-count", "integer", Number(container.CommonCount))) return;
                if (!sink.Add(prefix + "/key-count", "integer", Number(container.KeyCount))) return;
                // Inventory index and handle, plus occurrence for genuine duplicates, align relocated rows.
                var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
                // This hook aligns occupied identities across relocation, rather than physical vacancies.
                // The general view decoder includes every slot through F8 IncludeEmpty=true.
                foreach (var entry in container.Entries.Where(entry => !entry.IsEmpty))
                {
                    var identity = $"{prefix}/{entry.Group}[handle={entry.Handle:X8},index={entry.InventoryIndex}]";
                    var occurrence = occurrences.GetValueOrDefault(identity);
                    occurrences[identity] = occurrence + 1;
                    var path = occurrence == 0 ? identity : identity + $"[occurrence={occurrence + 1}]";
                    if (!sink.Add(path + "/quantity", "integer", Number(entry.Quantity))) return;
                    if (!sink.Add(path + "/identity", "text", entry.RawId)) return;
                }
            }
        }
    }

    private static string Number(uint value) => value.ToString(CultureInfo.InvariantCulture);
}
