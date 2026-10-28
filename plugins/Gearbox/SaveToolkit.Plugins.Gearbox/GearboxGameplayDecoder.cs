using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Gameplay-only F12 hook, independent of view pages and account/character identities.</summary>
internal sealed class GearboxGameplayDecoder : ISaveDecoderHook
{
    private const uint MoneyCategory = 618814354;
    public string Id => "gearbox-evidence";
    public bool CanDecode(ISaveFile file) => new GearboxPlugin().Probe(file).IsMatch;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes) throw new InvalidDataException("Gearbox evidence file exceeds decode limit.");
        if (sink.MaxDepth < 2 || sink.MaxValues <= 0)
        {
            sink.MarkTruncated("Gearbox evidence requires two path levels and a positive value budget.");
            return;
        }
        if (!CanDecode(file)) throw new InvalidDataException("Unsupported Gearbox evidence file.");
        var profile = file.FileName == "profile";
        var root = GearboxContainer.Parse(file.ReadAllBytes(), file.Directory.TitleId, profile).Decode();
        if (!profile)
        {
            if (!sink.Add("character/xp", "integer", root.Integer("experience_points").ToString(CultureInfo.InvariantCulture))) return;
            if (!sink.Add("currency/618814354", "integer", root.CategoryQuantity("inventory_category_list", MoneyCategory).ToString(CultureInfo.InvariantCulture))) return;
        }
        var lists = profile ? new[] { GearboxInventory.Bank, GearboxInventory.LostLoot } : [GearboxInventory.Backpack];
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in lists.SelectMany(root.All))
        {
            var serial = field.Message?.Last("item_serial_number")?.Value as byte[] ?? field.Value as byte[] ?? [];
            // Unchanged content aligns across reorder. Replacements remain removed/added; duplicates retain multiplicity.
            var hash = Convert.ToHexStringLower(SHA256.HashData(serial));
            var occurrence = occurrences.GetValueOrDefault(hash);
            occurrences[hash] = occurrence + 1;
            if (!sink.Add($"serials[{hash}:{occurrence}]/code", "bytes", Convert.ToBase64String(serial))) return;
        }
    }
}
