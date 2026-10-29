using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.EldenRing.Tests;

internal static class EvidencePrivacy
{
    // Mask everything except proven nonsecret numeric/inventory ranges, for BOTH endpoints.
    // Applying both masks keeps only the intersection safe if variable GaItem sizes move a range.
    public static IReadOnlyList<ChurnRule> Masks(ISaveFile file)
    {
        var bytes = file.ReadAllBytes();
        var save = EldenRingParser.Parse(bytes);
        var allowed = new List<(long Start, long End)>();
        foreach (var slot in save.Slots.Where(slot => slot.Active && slot.Version == 150))
        {
            var semantic = SemanticReader.Read(bytes, slot);
            foreach (var container in semantic.Containers)
                allowed.Add((container.Range.Offset, container.Range.Offset + container.Range.Length));
            foreach (var range in semantic.Ranges.Where(range => range.Name is "EquipData" or "ChrAsm" or "ChrAsm2" or "EquippedItems" or "QuickItems"))
                allowed.Add((range.Offset, range.Offset + range.Length));
            foreach (var link in EquipmentReader.Read(bytes, semantic))
            {
                if (ProfileEquipmentReader.Read(bytes, slot, link) is not { } profile) continue;
                allowed.Add((profile.EncodedOffset, profile.EncodedOffset + sizeof(uint)));
                allowed.Add((profile.ItemOffset, profile.ItemOffset + sizeof(uint)));
            }
            var player = slot.PlayerOffset!.Value;
            allowed.Add((player + 8, player + 0x6C));
            allowed.Add((player + 0xB7, player + 0xB8));
        }
        var masks = new List<ChurnRule>();
        long cursor = 0;
        foreach (var (start, end) in allowed.OrderBy(range => range.Start))
        {
            if (start > cursor) masks.Add(Mask(cursor, start - cursor));
            cursor = Math.Max(cursor, end);
        }
        if (cursor < file.Length) masks.Add(Mask(cursor, file.Length - cursor));
        return masks;
    }

    private static ChurnRule Mask(long start, long length) => new()
    {
        File = "memory.dat", Offset = start, Length = length,
        Reason = "Privacy exclusion: outside approved numeric/inventory ranges; may contain account/settings/passwords. Not proven churn.",
    };
}
