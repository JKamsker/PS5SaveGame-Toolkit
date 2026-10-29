using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>F12 hook for bounded, read-only semantic SaveDiff evidence independent of expanded UI pages.</summary>
internal sealed class RagnarokEvidenceDecoder : ISaveDecoderHook
{
    private readonly GodOfWarRagnarokPlugin _plugin = new();
    public string Id => "gow-ragnarok-evidence";

    public bool CanDecode(ISaveFile file) => _plugin.Probe(file).IsMatch &&
        !file.FileName.Equals("userpreferences", StringComparison.OrdinalIgnoreCase);

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes) throw new InvalidDataException("Ragnarök save exceeds the evidence decoder input bound.");
        if (sink.MaxDepth < 3) { sink.MarkTruncated("Ragnarök evidence paths require three components."); return; }
        var bytes = file.ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        var references = new RagnarokReferences(IReferenceContext.Empty);
        if (!sink.Add("header/kind", "integer", Number(RagnarokFormat.UInt32(bytes, RagnarokFormat.KindOffset))) ||
            !sink.Add("header/revision", "integer", $"0x{RagnarokFormat.UInt32(bytes, RagnarokFormat.RevisionOffset):X8}") ||
            !sink.Add("metadata/location", "text", index.Description)) return;
        foreach (var wallet in index.Nested.Wallets)
        {
            for (var row = 0; row < wallet.ResourceCount; row++)
            {
                var offset = wallet.ResourcesOffset + row * RagnarokFormat.WalletRowSize;
                var id = RagnarokFormat.UInt64(bytes, offset);
                var path = $"wallet[container={wallet.Id:X16};row={row};id={id:X16}]";
                if (!sink.Add(path + "/quantity", "integer", Number(RagnarokFormat.UInt32(bytes, offset + sizeof(ulong))),
                        Label(references.Find(id))) ||
                    !sink.Add(path + "/metadata", "integer", Number(RagnarokFormat.UInt32(bytes, offset + RagnarokFormat.WalletRowMetadataOffset)))) return;
            }
        }
        foreach (var probe in RagnarokAuthentication.Probe(bytes, index))
        {
            if (!sink.Add($"integrity[algorithm={probe.Algorithm};coverage={probe.Coverage}]/digest", "unkeyed-hypothesis", probe.Digest) ||
                !sink.Add($"integrity[algorithm={probe.Algorithm};coverage={probe.Coverage}]/matches", "count", Number(probe.MatchingOffsets.Count))) return;
        }
        if (!sink.Add("integrity/fileSha256", "sha256", Convert.ToHexString(SHA256.HashData(bytes)))) return;
        foreach (var group in index.Nested.FlagGroups)
        for (var row = 0; row < group.Count; row++)
        {
            var offset = group.RowsOffset + row * RagnarokFlagGroup.RowSize;
            var id = RagnarokFormat.UInt64(bytes, offset);
            if (!sink.Add($"flags[group={group.Id:X16};id={id:X16};row={row}]/value", "unknown-flags",
                    Convert.ToHexString(bytes.AsSpan(offset + sizeof(ulong), sizeof(uint))),
                    Label(references.Community.Tree(group.Id)))) return;
        }
        foreach (var gear in index.Gear.Records)
        {
            var record = references.Gear.Find(gear.Id);
            var root = $"gear[instance={gear.Instance}]";
            if (!sink.Add(root + "/definition", "inferred-definition", RagnarokReferences.Key(gear.Id), Label(record)) ||
                !sink.Add(root + "/catalog-level", "inferred-catalog-level", record?.Attributes.GetValueOrDefault("level") ?? "Unknown", Label(record)) ||
                !sink.Add(root + "/parent", "unknown-parent-index", Number(gear.Parent)) ||
                !sink.Add(root + "/subrecord-count", "unknown-role-count", Number(gear.SlotCount))) return;
        }
    }

    private static string Number<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
    private static string? Label(ReferenceRecord? record) => record is null ? null :
        $"{record.Name} · {record.Description} {record.AttributionText}";
}
