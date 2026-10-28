using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Read-only F12 hook for inventory evidence. Avoids hundreds of thousands of fog
/// byte cells and excludes campaign IDs, timestamps and all system files.</summary>
internal sealed class RiftEvidenceDecoder : ISaveDecoderHook
{
    public string Id => "insomniac-evidence";
    public bool CanDecode(ISaveFile file) =>
        string.Equals(file.Directory.TitleId, "PPSA01474", StringComparison.OrdinalIgnoreCase) &&
        (file.FileName is "i29-s.save" or "i29-userprefs.save") && new InsomniacPlugin().Probe(file).IsMatch;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes) throw new InvalidDataException("File exceeds decoder limit.");
        var container = Dat1Container.Parse(file.ReadAllBytes());
        DdlSections sections;
        try
        {
            sections = new DdlSections(container, sink.MaxDepth);
        }
        catch (DdlDepthLimitException)
        {
            sink.MarkTruncated("DDL parsing stopped at the host's depth limit.");
            return;
        }
        if (sections.Roots.TryGetValue(DdlSections.PreferencesTag, out var preferences))
        {
            foreach (var field in preferences.Values.Where(f => f.Kind == DdlValueKind.Bool && f.Values.Count == 1))
                if (!Add(sink, $"preferences/{field.Name}", field.Kind.ToString(), field.Values[0].ToString())) return;
            return;
        }
        if (!sections.Roots.TryGetValue(RiftPlayerView.CampaignTag, out var campaign))
            throw new SaveFormatException("Rift Apart evidence requires a campaign or preferences section.");
        foreach (var field in campaign.Values.Where(f => f.Name.StartsWith("Career", StringComparison.Ordinal) ||
                     f.Name.StartsWith("Level", StringComparison.Ordinal) || f.Name.StartsWith("QuickSelect", StringComparison.Ordinal) ||
                     f.Name is "SpentBolts" or "VanityPickupsCollected" or "RYNOPlansCollected" or "RuinTokensCollected" or "ZerpStonesCollected"))
        {
            if (!Add(sink, $"campaign/{field.Name}/wire-type", "type", field.Kind.ToString())) return;
            for (var index = 0; index < field.Values.Count; index++)
                if (!Add(sink, $"campaign/{field.Name}/{index}", field.Kind.ToString(), field.Values[index].ToString()))
                    return;
        }
        foreach (var field in sections.Roots[RiftPlayerView.GoldBoltTag].Values.Where(f => f.Values.Count == 1))
            if (!Add(sink, $"gold-options/{field.Name}", field.Kind.ToString(), field.Values[0].ToString())) return;
        var blocks = InventoryReader.Read(container);
        if (blocks.Any(block => block.Occurrence > 0))
            sink.Warn("Repeated inventory tags are aligned by stored occurrence; container reorder is not proven identity.");
        foreach (var block in blocks)
        {
            var prefix = $"inventory/{block.Key}";
            if (!Add(sink, $"{prefix}/record-count", "integer", block.Records.Count.ToString(CultureInfo.InvariantCulture))) return;
            foreach (var record in block.Records)
            {
                var path = $"{prefix}/{record.Key}";
                if (record.GuidBytes is null || !record.Key.EndsWith("/0", StringComparison.Ordinal))
                    sink.MarkTruncated("Inventory identity uses a position or duplicate-GUID occurrence; absence is not proof of removal.");
                if (!Add(sink, $"{path}/payload-sha256", "hash",
                        Convert.ToHexStringLower(SHA256.HashData(container.Bytes.AsSpan(record.Offset, record.Size)))))
                    return;
                if (record.Count is { } count && !Add(sink, $"{path}/count", "integer", count.ToString(CultureInfo.InvariantCulture))) return;
                if (record.Xp is { } xp)
                {
                    if (!Add(sink, $"{path}/xp", "integer", xp.ToString(CultureInfo.InvariantCulture))) return;
                    if (!Add(sink, $"{path}/loaded-ammo", "float", record.LoadedAmmo!.Value.ToString("R", CultureInfo.InvariantCulture))) return;
                }
                if (record.Upgrades is { } upgrades)
                {
                    if (!Add(sink, $"{path}/mods", "mask", upgrades.Mods.ToString("x16", CultureInfo.InvariantCulture))) return;
                    if (!Add(sink, $"{path}/omega", "boolean", upgrades.Omega.ToString(CultureInfo.InvariantCulture))) return;
                    for (var index = 0; index < upgrades.Grid.Count; index++)
                        if (!Add(sink, $"{path}/mod-grid/{index}", "u16", upgrades.Grid[index].ToString(CultureInfo.InvariantCulture))) return;
                    for (var index = 0; index < upgrades.Counts.Count; index++)
                        if (!Add(sink, $"{path}/mod-count/{index}", "u8", upgrades.Counts[index].ToString(CultureInfo.InvariantCulture))) return;
                }
                if (record.Vanity is { } vanity &&
                    !Add(sink, $"{path}/color", "u8", vanity.Color.ToString(CultureInfo.InvariantCulture))) return;
            }
        }
        foreach (var tag in new[] { DdlSections.DimensionTag, DdlSections.ObjectivesTag, DdlSections.AwardsTag, DdlSections.FogTag })
        {
            if (!sections.Roots.TryGetValue(tag, out var root)) continue;
            foreach (var field in root.Values)
            {
                var count = field.Objects.Sum(o => o.Fields.Count);
                if (!Add(sink, $"systems/{tag:x8}/{field.Name}/child-fields", "integer", count.ToString(CultureInfo.InvariantCulture))) return;
            }
        }
    }

    private static bool Add(ISaveDecodeSink sink, string path, string kind, string value)
    {
        if (path.Count(character => character == '/') > sink.MaxDepth)
        {
            sink.MarkTruncated("Evidence traversal stopped at the host's depth limit.");
            return false;
        }
        return sink.Add(path, kind, value);
    }
}
