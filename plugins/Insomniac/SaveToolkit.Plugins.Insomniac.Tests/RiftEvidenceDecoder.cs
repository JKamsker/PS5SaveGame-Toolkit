using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

/// <summary>F1 adapter for inventory research. Avoids hundreds of thousands of fog
/// byte cells and excludes campaign IDs, timestamps and all system files.</summary>
internal sealed class RiftEvidenceDecoder : ISaveDecoder
{
    public string Id => "insomniac-evidence";
    public bool CanDecode(ISaveFile file) => file.FileName == "i29-s.save";

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes) throw new InvalidDataException("File exceeds decoder limit.");
        var container = Dat1Container.Parse(file.ReadAllBytes());
        var sections = new DdlSections(container);
        var builder = new SemanticTreeBuilder(Id, limits);
        var campaign = sections.Roots[RiftPlayerView.CampaignTag];
        foreach (var field in campaign.Values.Where(f => f.Name.StartsWith("Career", StringComparison.Ordinal) ||
                     f.Name.StartsWith("Level", StringComparison.Ordinal) || f.Name.StartsWith("QuickSelect", StringComparison.Ordinal)))
        {
            builder.Add($"campaign/{field.Name}/wire-type", "type", field.Kind.ToString());
            for (var index = 0; index < field.Values.Count; index++)
                if (!builder.Add($"campaign/{field.Name}/{index}", field.Kind.ToString(), field.Values[index].ToString()))
                    return builder.Build();
        }
        var inventory = container.Sections.Single(s => s.Tag == InventoryReader.SectionTag);
        foreach (var block in InventoryReader.Read(container, inventory))
        {
            var prefix = $"inventory/{block.Key}";
            builder.Add($"{prefix}/record-count", "integer", block.Records.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var record in block.Records)
            {
                var path = $"{prefix}/{record.Key}";
                if (!builder.Add($"{path}/payload-sha256", "hash",
                        Convert.ToHexStringLower(SHA256.HashData(container.Bytes.AsSpan(record.Offset, record.Size)))))
                    return builder.Build();
                if (record.Count is { } count) builder.Add($"{path}/count", "integer", count.ToString(CultureInfo.InvariantCulture));
                if (record.Xp is { } xp)
                {
                    builder.Add($"{path}/xp", "integer", xp.ToString(CultureInfo.InvariantCulture));
                    builder.Add($"{path}/loaded-ammo", "float", record.LoadedAmmo!.Value.ToString("R", CultureInfo.InvariantCulture));
                }
            }
        }
        foreach (var tag in new[] { DdlSections.DimensionTag, DdlSections.ObjectivesTag, DdlSections.AwardsTag, DdlSections.FogTag })
        {
            foreach (var field in sections.Roots[tag].Values)
            {
                var count = field.Objects.Sum(o => o.Fields.Count);
                builder.Add($"systems/{tag:x8}/{field.Name}/child-fields", "integer", count.ToString(CultureInfo.InvariantCulture));
            }
        }
        return builder.Build();
    }
}
