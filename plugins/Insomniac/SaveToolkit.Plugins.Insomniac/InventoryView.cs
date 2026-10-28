using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class InventoryView(IReadOnlyList<InventoryBlock> blocks, IReferenceCatalog? types,
    Func<string, string, FieldSpec, FieldValue, FieldNode> bind,
    Dictionary<string, Func<IReadOnlyList<ViewNode>>> lazy, RiftCollections collections)
{
    private readonly Dictionary<string, QueryableTableNode> _tables = new(StringComparer.Ordinal);
    internal void RegisterCollections()
    {
        foreach (var block in blocks) _ = Records(block);
    }

    internal IReadOnlyList<ViewNode> Build() =>
    [
        new TextNode("Raw inventory diagnostics. Named path/token joins and selected state appear in Weapons, Armour and Upgrades. Repeated blocks can hold shared, equipped or cached state; actor/ownership and upgrade limits remain unproven. GUID/config columns preserve exact stored byte tokens.") { Tone = Tone.Warning },
        new StatsNode([new("Bounded TVNI blocks", blocks.Count.ToString(CultureInfo.InvariantCulture)),
            new("Serialized records", blocks.Sum(b => b.Records.Count).ToString(CultureInfo.InvariantCulture))]),
        .. blocks.Select(block =>
        {
            var id = $"inventory/{block.Key}";
            lazy[id] = () => Records(block);
            return (ViewNode)new SectionNode($"Container 0x{block.Tag:X8} · occurrence {block.Occurrence}", [])
            {
                LazyId = id,
                Badge = $"{block.Records.Count} records",
                Description = $"Offset 0x{block.Offset:X}; {block.Size} bytes; {block.OpaqueBytes} opaque trailing bytes retained. Actor/ownership mapping is unknown.",
            };
        }),
    ];

    private IReadOnlyList<ViewNode> Records(InventoryBlock block)
    {
        if (block.Records.Count == 0)
            return [new TextNode($"No serialized item records. {block.OpaqueBytes} trailing state bytes remain opaque.") { Tone = Tone.Muted }];
        if (!_tables.TryGetValue(block.Key, out var table))
        {
            table = collections.Add($"inventory/{block.Key}", $"Container {block.Key}",
                [new("type", "Schema type"), new("guid", "Item GUID bytes (candidate)"),
                 new("config", "Config bytes (candidate)"), new("count", "Count (candidate)"), new("state", "State candidates")],
                block.Records.Select(record => Row(block, record)),
                "Type IDs match the CC0 schema. Field meanings are inferred from matching fixed payloads, not controlled gameplay captures. " + DdlSections.Attribution);
            _tables.Add(block.Key, table);
        }
        return [table];
    }

    private RiftRow Row(InventoryBlock block, InventoryRecord record)
    {
        var prefix = $"inventory/{block.Key}/{record.Key}";
        var type = types?.Find($"{record.Type:x8}");
        var detailId = $"{prefix}/details";
        return new RiftRow(record.Key, new Dictionary<string, RiftCell>
        {
            ["type"] = RiftCollections.Field(type?.Name ?? $"0x{record.Type:X8}", () => bind($"{prefix}/type-name", "Schema type",
                new ReferenceSpec("rift-item-types") { AllowCustom = true }, FieldValue.Of($"{record.Type:x8}")) with
            {
                Confidence = type is null ? Confidence.Unknown : Confidence.Verified,
                Description = $"Stored type ID: 0x{record.Type:X8}. " + (type?.AttributionText ?? "Unknown serialized type; full payload retained."),
            }),
            ["guid"] = RiftCollections.Field(record.GuidBytes ?? "Opaque", () => Token($"{prefix}/guid", record.GuidBytes)),
            ["config"] = RiftCollections.Field(record.ConfigBytes ?? "Opaque", () => Token($"{prefix}/config", record.ConfigBytes)),
            ["count"] = record.Count is { } count
                ? RiftCollections.Field(count.ToString(CultureInfo.InvariantCulture),
                    () => Candidate($"{prefix}/count", "Item count candidate", IntegerSpec.UInt32, FieldValue.Of((ulong)count)), count)
                : RiftCell.TextOnly("Opaque"),
            ["state"] = new("Raw state candidates", () =>
            {
                lazy[detailId] = () => State(prefix, record);
                return new SectionNode("Raw state candidates", []) { LazyId = detailId };
            }),
        });
    }

    private IReadOnlyList<ViewNode> State(string prefix, InventoryRecord record)
    {
        var result = new List<ViewNode>
        {
            new TextNode($"Payload at 0x{record.Offset:X}; {record.Size} bytes. Unknown fields and presence masks are preserved verbatim.") { Monospace = true },
        };
        if (record.Xp is { } xp)
        {
            result.Add(Candidate($"{prefix}/xp", "XP candidate (BE u32)", IntegerSpec.UInt32, FieldValue.Of((ulong)xp)));
            result.Add(Candidate($"{prefix}/checkpoint-ammo", "Last checkpoint ammo candidate (BE f32)", FloatSpec.Single, FieldValue.Of(record.CheckpointAmmo!.Value)));
            result.Add(Candidate($"{prefix}/loaded-ammo", "Loaded ammo candidate (BE f32)", FloatSpec.Single, FieldValue.Of(record.LoadedAmmo!.Value)));
        }
        return result;
    }

    private FieldNode Token(string id, string? value) => bind(id, "Stored identity bytes", new TextSpec(), FieldValue.Of(value ?? "Opaque")) with
    {
        Confidence = value is null ? Confidence.Unknown : Confidence.Inferred,
        Description = "Exact token from the fixed-mask payload; schema GUID/config slot interpretation is inferred. " + DdlSections.Attribution,
    };

    private FieldNode Candidate(string id, string label, FieldSpec spec, FieldValue value) => bind(id, label, spec, value) with
    {
        Confidence = Confidence.Inferred,
        Description = "Candidate schema slot corroborated across sample generations. Controlled HUD/action pairs are still required. " + DdlSections.Attribution,
        Warning = "Read-only; no ownership, wallet, level, ammo-cap or upgrade rule is inferred from this value.",
    };
}
