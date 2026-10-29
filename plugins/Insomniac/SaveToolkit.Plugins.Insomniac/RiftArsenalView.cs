using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class RiftArsenalView
{
    private const uint WeaponType = 0xA89E_9C3C;
    private const uint MeleeType = 0x2F1E_071F;
    private readonly Func<string, string, FieldSpec, FieldValue, FieldNode> _bind;
    private readonly Dictionary<string, ReferenceRecord> _names = new(StringComparer.Ordinal);
    private readonly QueryableTableNode _weapons;
    private readonly QueryableTableNode _upgrades;
    private readonly QueryableTableNode _graphs;
    private readonly QueryableTableNode? _referenceUpgrades;
    private const string Boundary = "Serialized record presence is not ownership. Repeated containers can contain cached, shared or equipment state. No character assignment, level conversion, capacity or upgrade dependency is assumed.";

    internal RiftArsenalView(IReadOnlyList<InventoryBlock> blocks, IReferenceCatalog? names, IReferenceCatalog? upgrades,
        RiftCollections collections, Func<string, string, FieldSpec, FieldValue, FieldNode> bind,
        Dictionary<string, Func<IReadOnlyList<ViewNode>>> lazy)
    {
        _bind = bind;
        _referenceUpgrades = RiftCatalogPresentation.Upgrades(upgrades, collections);
        if (names is not null)
            for (var offset = 0; offset < names.Count; offset += QueryableCollectionLimits.MaxPageSize)
                foreach (var record in names.Search(new() { Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize }).Items)
                    _names.TryAdd(RiftAssetId.Hash(record.Key), record);
        var records = blocks.SelectMany(block => block.Records.Where(r => r.Type is WeaponType or MeleeType)
            .Select(record => (Block: block, Record: record))).ToArray();
        _weapons = collections.Add("arsenal/weapons", "Weapon records",
            [new("icon", "Reference icon"), new("name", "Weapon"), new("container", "Container"), new("kind", "Kind"),
             new("config", "Config token"), new("xp", "XP candidate", IntegerSpec.UInt32),
             new("ammo", "Loaded ammo candidate", FloatSpec.Single), new("omega", "Omega candidate")],
            records.Select(pair => WeaponRow(pair.Block, pair.Record)), Boundary + " " +
                RiftCatalogPresentation.ArtAttribution + " " + DdlSections.Attribution);
        _upgrades = collections.Add("arsenal/upgrades", "Weapon upgrade state",
            [new("name", "Weapon"), new("container", "Container"), new("kind", "Schema slot"),
             new("index", "Index", IntegerSpec.UInt16), new("value", "Stored value", IntegerSpec.UInt64)],
            records.SelectMany(pair => UpgradeRows(pair.Block, pair.Record)),
            "ModGrid contains sixteen stored row masks; ModCount contains thirty-two counters. These are not named upgrade nodes or costs. Bit order, prerequisites and purchased-node meaning need controlled grid captures. " + DdlSections.Attribution);
        _graphs = collections.Add("arsenal/graphs", "Stored upgrade diagrams",
            [new("name", "Weapon candidate"), new("container", "Container"), new("diagram", "Diagram and text values")],
            records.Where(pair => pair.Record.Upgrades is not null).Select(pair =>
            {
                var id = $"upgrades/{pair.Block.Key}/{pair.Record.Key}";
                return new RiftRow(id, new Dictionary<string, RiftCell>
                {
                    ["name"] = Name(id, pair.Record),
                    ["container"] = RiftCell.TextOnly(pair.Block.Key),
                    ["diagram"] = new("Stored grid diagram", () => RiftUpgradeGraph.Lazy(pair.Block, pair.Record, lazy, bind)),
                });
            }), RiftUpgradeGraph.Boundary + DdlSections.Attribution);
    }

    internal IReadOnlyList<ViewNode> Weapons() => [new TextNode(Boundary) { Tone = Tone.Warning }, _weapons];
    internal IReadOnlyList<ViewNode> Upgrades() =>
    [
        new TextNode("Read-only upgrade state. Search by weapon or container, then compare row masks/counters with the in-game grid. A mask bit is not presented as a proven purchase or prerequisite.") { Tone = Tone.Warning },
        _upgrades,
        _graphs,
        .. _referenceUpgrades is { } reference ? new ViewNode[] { reference } : [],
    ];

    private RiftRow WeaponRow(InventoryBlock block, InventoryRecord record)
    {
        var id = $"arsenal/{block.Key}/{record.Key}";
        return new(id, new Dictionary<string, RiftCell>
        {
            ["icon"] = RiftCatalogPresentation.Image(record.ConfigBytes is { } config ? _names.GetValueOrDefault(config) : null),
            ["name"] = Name(id, record),
            ["container"] = RiftCell.TextOnly(block.Key),
            ["kind"] = RiftCell.TextOnly(record.Type == MeleeType ? "Melee schema" : "Weapon schema"),
            ["config"] = Config(id, record),
            ["xp"] = record.Xp is { } xp ? Value(id + "/xp", "XP candidate", xp) : RiftCell.TextOnly("Opaque"),
            ["ammo"] = record.LoadedAmmo is { } ammo ? RiftCollections.Field(ammo.ToString("R", CultureInfo.InvariantCulture),
                () => Candidate(id + "/ammo", "Loaded ammo candidate", FloatSpec.Single, FieldValue.Of(ammo)),
                Math.Abs((double)ammo) <= (double)decimal.MaxValue ? (decimal)ammo : null) : RiftCell.TextOnly("Opaque"),
            ["omega"] = record.Upgrades is { } upgrade ? RiftCollections.Field(upgrade.Omega.ToString(),
                () => Candidate(id + "/omega", "Omega candidate", new BoolSpec(), FieldValue.Of(upgrade.Omega))) : RiftCell.TextOnly("Opaque"),
        });
    }

    private IEnumerable<RiftRow> UpgradeRows(InventoryBlock block, InventoryRecord record)
    {
        if (record.Upgrades is not { } state) yield break;
        var prefix = $"upgrades/{block.Key}/{record.Key}";
        yield return Row("Mods", 0, state.Mods);
        for (var index = 0; index < state.Grid.Count; index++) yield return Row("ModGrid row", index, state.Grid[index]);
        for (var index = 0; index < state.Counts.Count; index++) yield return Row("ModCount slot", index, state.Counts[index]);
        yield return Row("SelectedCell", 0, state.SelectedCell);

        RiftRow Row(string kind, int index, ulong value)
        {
            var id = $"{prefix}/{kind}/{index}";
            return new(id, new Dictionary<string, RiftCell>
            {
                ["name"] = Name(prefix, record), ["container"] = RiftCell.TextOnly(block.Key),
                ["kind"] = RiftCell.TextOnly(kind), ["index"] = RiftCollections.Number(index),
                ["value"] = Value(id, kind, value),
            });
        }
    }

    private RiftCell Name(string id, InventoryRecord record)
    {
        var reference = record.ConfigBytes is { } config ? _names.GetValueOrDefault(config) : null;
        var label = reference?.Name ?? $"Unresolved {record.ConfigBytes ?? $"type {record.Type:x8}"}";
        return RiftCollections.Field(label, () => _bind(id + "/name", "Weapon name candidate", new TextSpec(), FieldValue.Of(label)) with
        {
            Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
            Description = reference is null ? "No matching pinned config-path catalog entry. Exact config token retained." :
                $"Config path: {reference.Key}. {reference.Description} {reference.AttributionText}",
        });
    }

    private RiftCell Config(string id, InventoryRecord record)
    {
        var reference = record.ConfigBytes is { } token ? _names.GetValueOrDefault(token) : null;
        var key = reference?.Key ?? record.ConfigBytes ?? "Opaque";
        return RiftCollections.Field(key, () => _bind(id + "/config", "Config identity candidate",
            new ReferenceSpec("rift-weapons") { AllowCustom = true }, FieldValue.Of(key)) with
        {
            Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
            Description = $"Exact stored config bytes: {record.ConfigBytes ?? "opaque"}. " +
                (reference?.AttributionText ?? "No catalog join; raw token preserved."),
            Warning = Boundary,
        });
    }

    private RiftCell Value(string id, string label, ulong value) => RiftCollections.Field(
        value.ToString(CultureInfo.InvariantCulture), () => Candidate(id, label, IntegerSpec.UInt64, FieldValue.Of(value)), value);

    private FieldNode Candidate(string id, string label, FieldSpec spec, FieldValue value) => _bind(id, label, spec, value) with
    {
        Confidence = Confidence.Inferred,
        Description = "Full-mask schema slot; PS5 byte order checked across the corpus. " + DdlSections.Attribution,
        Warning = "Read-only. Gameplay meaning and limits require independent HUD/action captures.",
    };
}
