using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class RiftArmourView
{
    private const uint VanityType = 0xF6AD_1389;
    private const int MaskBits = 64;
    private readonly Func<string, string, FieldSpec, FieldValue, FieldNode> _bind;
    private readonly IReferenceCatalog? _catalog;
    private readonly QueryableTableNode _collection;
    private readonly QueryableTableNode _appearance;

    internal RiftArmourView(DdlSections sections, IReadOnlyList<InventoryBlock> blocks,
        IReferenceCatalog? catalog, RiftCollections collections,
        Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        _bind = bind;
        _catalog = catalog;
        var campaign = sections.Roots.GetValueOrDefault(RiftPlayerView.CampaignTag);
        var field = campaign?.GetValueOrDefault("VanityPickupsCollected");
        ulong? mask = field is { Values.Count: 1 } ? field.Values[0].AsUInt64() : null;
        var viewed = campaign?.GetValueOrDefault("NewItemsViewed")?.Values.Select(v => v.AsString()).ToHashSet(StringComparer.Ordinal) ?? [];
        _collection = collections.Add("armour/collection", "Armour collection checklist",
            [new("name", "Piece candidate"), new("set", "Set"), new("slot", "Slot"),
             new("state", "Collection bit"), new("viewed", "Menu viewed flag"), new("perk", "Catalog bonus category")],
            mask is { } value ? Enumerable.Range(0, MaskBits).Select(bit => CollectionRow(bit, value, viewed)) : [],
            "VanityPickupsCollected bits joined to CC0 VanityBundleSaveId ordinals (inferred). Clear bits do not establish availability. Catalog bonuses describe collecting pieces, not wearing them; no effective bonus percentage is calculated.");
        _appearance = collections.Add("armour/appearance", "Vanity appearance records",
            [new("container", "Container"), new("config", "Config token"), new("id", "Item ID candidate", IntegerSpec.UInt16),
             new("color", "Color candidate", IntegerSpec.UInt8), new("preview", "BeenPreviewed candidate", IntegerSpec.UInt8)],
            blocks.SelectMany(block => block.Records.Where(r => r.Type == VanityType).Select(record => AppearanceRow(block, record))),
            "Serialized VanityItemPrius records include appearance/customization state. No equipped head/torso/legs assignment or join to collected-set bonuses is asserted. " + DdlSections.Attribution);
    }

    internal IReadOnlyList<ViewNode> Build() =>
    [
        new TextNode("Collection bonuses and equipped appearance are separate. The checklist uses inferred schema ordinal joins; appearance records retain exact config tokens until config assets or equip captures establish their identities.") { Tone = Tone.Warning },
        _collection,
        _appearance,
    ];

    private RiftRow CollectionRow(int bit, ulong mask, HashSet<string> viewed)
    {
        var key = bit.ToString(CultureInfo.InvariantCulture);
        var reference = _catalog?.Find(key);
        var set = reference?.Category ?? "Unresolved";
        var label = reference?.Name ?? $"Unresolved armour bit {bit}";
        var rawName = reference?.Attributes.GetValueOrDefault("saveId");
        var isSet = (mask & (1UL << bit)) != 0;
        return new(key, new Dictionary<string, RiftCell>
        {
            ["name"] = RiftCollections.Field(label, () => _bind($"armour/bit/{bit}/name", "Piece candidate", new TextSpec(), FieldValue.Of(label)) with
            {
                Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
                Description = $"Stored bit {bit}; schema name {rawName ?? "unresolved"}. {reference?.Description} {reference?.AttributionText ?? DdlSections.Attribution}",
            }),
            ["set"] = Attributed("set", set),
            ["slot"] = Attributed("slot", reference?.Attributes.GetValueOrDefault("slot") ?? "Unresolved"),
            ["state"] = RiftCollections.Field(isSet ? "Set" : "Not set", () => _bind($"armour/bit/{bit}/set", "Collection bit", new BoolSpec(), FieldValue.Of(isSet)) with
            {
                Confidence = Confidence.Verified,
                Description = $"Bit {bit} of stored VanityPickupsCollected. Collection-to-piece interpretation remains inferred. " + DdlSections.Attribution,
            }),
            ["viewed"] = RiftCell.TextOnly(rawName is not null && viewed.Contains(rawName) ? "Present" : "Absent / unresolved"),
            ["perk"] = RiftCollections.Field(reference?.Attributes.GetValueOrDefault("perk") ?? "Unknown",
                () => _bind($"armour/bit/{bit}/perk", "Catalog bonus category", new TextSpec(),
                    FieldValue.Of(reference?.Attributes.GetValueOrDefault("perk") ?? "Unknown")) with
                {
                    Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
                    Description = reference?.AttributionText ?? "No matching catalog entry.",
                    Warning = "Catalog description only; effective bonus and entitlement availability are not proven by this bit.",
                }),
        });

        RiftCell Attributed(string column, string text) => RiftCollections.Field(text,
            () => _bind($"armour/bit/{bit}/catalog/{column}", column, new TextSpec(), FieldValue.Of(text)) with
            {
                Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
                Description = reference?.AttributionText ?? "No matching catalog entry.",
            });
    }

    private RiftRow AppearanceRow(InventoryBlock block, InventoryRecord record)
    {
        var id = $"armour/appearance/{block.Key}/{record.Key}";
        return new(id, new Dictionary<string, RiftCell>
        {
            ["container"] = RiftCell.TextOnly(block.Key), ["config"] = RiftCell.TextOnly(record.ConfigBytes ?? "Opaque"),
            ["id"] = Number("id", record.ItemId), ["color"] = Number("color", record.Vanity?.Color),
            ["preview"] = Number("preview", record.Vanity?.BeenPreviewed),
        });
        RiftCell Number(string label, long? value) => value is { } number
            ? RiftCollections.Field(number.ToString(CultureInfo.InvariantCulture), () => _bind($"{id}/{label}", label,
                IntegerSpec.UInt32, FieldValue.Of(number)) with
            {
                Confidence = Confidence.Inferred,
                Description = "Supported full-mask VanityItemPrius slot. " + DdlSections.Attribution,
            }, number) : RiftCell.TextOnly("Opaque");
    }
}
