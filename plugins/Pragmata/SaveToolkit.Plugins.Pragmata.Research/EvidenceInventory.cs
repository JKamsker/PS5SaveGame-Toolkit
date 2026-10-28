using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Pragmata.Research;

internal sealed class EvidenceInventory(SchemaCatalog schema)
{
    private readonly Dictionary<uint, List<(SaveNode Section, byte[] Bytes)>> _sections = [];
    private readonly Dictionary<(string Family, string Path, string Id), HashSet<string>> _ids = [];
    private readonly List<object> _samples = [];
    private readonly HashSet<int> _generations = [];

    public void Add(ISaveFile file)
    {
        var original = file.ReadAllBytes();
        var sample = Convert.ToHexStringLower(SHA256.HashData(original));
        var bytes = new DsssContainer(original).Plaintext;
        var layout = new SaveLayout(bytes);
        _samples.Add(new { file = file.FileName, generation = file.Directory.Generation, sha256 = sample });
        _generations.Add(file.Directory.Generation);
        if (layout.Header is null) return;
        foreach (var section in layout.Sections)
        {
            if (!_sections.TryGetValue(section.ClassHash, out var instances))
                _sections[section.ClassHash] = instances = [];
            instances.Add((section, bytes));
            var family = schema.Name(section.ClassHash) ?? $"{section.ClassHash:x8}";
            if (family.Contains("Inventory", StringComparison.Ordinal) || family.Contains("Weapon", StringComparison.Ordinal) ||
                family.Contains("Enhance", StringComparison.Ordinal) || family.Contains("Equipment", StringComparison.Ordinal) ||
                family.Contains("Objective", StringComparison.Ordinal) || family.Contains("Occurrence", StringComparison.Ordinal) ||
                family.Contains("CheckPoint", StringComparison.Ordinal) || family.Contains("Chapter", StringComparison.Ordinal))
                CollectIds(section, $"section[class={section.ClassHash:x8}]", family, bytes, sample);
        }
        CollectIds(layout.Header, "header", "header", bytes, sample);
    }

    public object Build() => new
    {
        format = "pragmata-evidence/1", version = "1.0.0", titleId = "PPSA02530", source = SchemaCatalog.Source,
        license = "MIT", licenseUrl = SchemaCatalog.LicenseUrl, retrieved = "2026-10-28",
        schemaSha256 = SchemaCatalog.Sha256,
        evidence = "Identifier spelling is hash-verified; IDs are observed facts. Meanings, limits and effects are Unknown. Slot differences are uncontrolled, not causal evidence.",
        generations = _generations.Order().ToArray(), samples = _samples,
        sections = _sections.OrderBy(p => p.Key).Select(p => Section(p.Key, p.Value)).ToArray(),
        identifiers = _ids.OrderBy(p => p.Key.Family, StringComparer.Ordinal).ThenBy(p => p.Key.Path, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Id, StringComparer.Ordinal).Select(p => new
            {
                family = p.Key.Family, path = p.Key.Path, id = p.Key.Id, samples = p.Value.Count,
                valueConfidence = "Verified", semanticConfidence = "Unknown", source = "PPSA02530 observed sample values; no player-facing mapping",
            }).ToArray(),
    };

    private object Section(uint hash, List<(SaveNode Section, byte[] Bytes)> samples) => new
    {
        hash = $"{hash:x8}", name = schema.Name(hash), nameConfidence = schema.Name(hash) is null ? "Unknown" : "Verified",
        semanticsConfidence = "Inferred", samples = samples.Count,
        fields = samples.SelectMany(s => s.Section.Children).Select(n => n.Hash).Distinct().Order().Select(fieldHash =>
        {
            var fields = samples.Select(s => (Node: s.Section.Find(fieldHash), s.Bytes)).Where(s => s.Node is not null).ToArray();
            return new
            {
                path = $"section[class={hash:x8}]/{fieldHash:x8}", hash = $"{fieldHash:x8}", name = schema.Name(fieldHash),
                declaredType = schema.DeclaredType(hash, fieldHash), storedTypes = fields.Select(s => FieldTags.ShortName(s.Node!.Type)).Distinct().Order().ToArray(),
                samples = fields.Length, minimumCount = fields.Min(s => s.Node!.Children.Count), maximumCount = fields.Max(s => s.Node!.Children.Count),
                valueVariants = fields.Select(s => Fingerprint(s.Node!, s.Bytes)).Distinct().Count(), semanticConfidence = "Unknown",
            };
        }).ToArray(),
    };

    private void CollectIds(SaveNode node, string path, string family, byte[] bytes, string sample)
    {
        var name = HashNames.All.GetValueOrDefault(node.Index >= 0 ? node.Parent!.Hash : node.Hash, "");
        if ((node.IsPrimitive && node.Type is not (FieldTags.Boolean or FieldTags.Float32 or FieldTags.Float64) ||
             node.Type == FieldTags.Struct && node.Size == 16) &&
            (name.Contains("Id", StringComparison.OrdinalIgnoreCase) || name.Contains("Hash", StringComparison.Ordinal) ||
             name is "CurrentChapters" or "_EventType" || node.Hash == KnownHashes.ItemId) && name is not ("_Hash" or "_Version"))
        {
            var id = node.IsPrimitive ? PrimitiveCodec.Read(node, bytes).ToString() :
                "opaque-sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(node.Offset, node.Size)));
            var key = (family, path, id);
            if (!_ids.TryGetValue(key, out var sources)) _ids[key] = sources = new(StringComparer.Ordinal);
            sources.Add(sample);
        }
        foreach (var child in node.Children)
            CollectIds(child, child.Index >= 0 ? path + "[]" : $"{path}/{child.Hash:x8}", family, bytes, sample);
    }

    private static string Fingerprint(SaveNode node, byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(node);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
        void Append(SaveNode current)
        {
            Span<byte> metadata = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(metadata, current.Hash);
            BinaryPrimitives.WriteInt32LittleEndian(metadata[4..], current.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(metadata[8..], current.ClassHash);
            BinaryPrimitives.WriteInt32LittleEndian(metadata[12..], current.Children.Count);
            hash.AppendData(metadata);
            if (current.Type is not (FieldTags.Class or FieldTags.Array)) hash.AppendData(bytes.AsSpan(current.Offset, current.Size));
            foreach (var child in current.Children) Append(child);
        }
    }
}
