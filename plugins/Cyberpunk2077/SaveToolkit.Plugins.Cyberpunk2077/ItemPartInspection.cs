using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>One searchable table per attachment tree; paths retain their existing row identities.</summary>
internal sealed class ItemPartInspection
{
    public const string CollectionPrefix = "item-parts/";
    private readonly InspectionCollection<(string Path, ItemPart Part)> _collection;

    public static string IdFor(int nodeId) => CollectionPrefix + nodeId.ToString(CultureInfo.InvariantCulture);

    public ItemPartInspection(int nodeId, ItemPart root, CyberpunkReferences references)
    {
        var parts = ExtendedItemReader.Parts(root).ToArray();
        var values = new Dictionary<string, Func<(string Path, ItemPart Part), string>>
        {
            ["path"] = pair => pair.Path,
            ["item"] = pair => references.ItemDisplayName(pair.Part.Identity.TweakId) ?? $"0x{pair.Part.Identity.TweakId:X16}",
            ["identity"] = pair => pair.Part.Identity.ToString(),
            ["appearance"] = pair => pair.Part.Appearance,
            ["slot"] = pair => $"0x{pair.Part.AttachmentSlot:X16}",
            ["extra"] = pair => $"Opaque uint32 0x{pair.Part.Unknown:X8}; loot 0x{pair.Part.Additional.LootPool:X16}; " +
                $"additional 0x{pair.Part.Additional.Unknown:X8}; raw level {pair.Part.Additional.RequiredLevel.ToString("R", CultureInfo.InvariantCulture)}",
        };
        var collectionId = IdFor(nodeId);
        var metadata = new QueryableCollection(collectionId, "Attachment / part tree",
            [new("path", "Part path"), new("item", "Record identifier"), new("identity", "Identity"),
                new("appearance", "Appearance string"), new("slot", "Attachment slot ID"), new("extra", "Raw extra data")], parts.Length)
        {
            Description = ExtendedItemReader.Source + " Inferred attachment-tree schema; parts are not independent inventory instances. " +
                "Unknown or zero identifiers are retained; no vacant-part sentinel is confirmed.",
        };
        _collection = new(metadata, parts, pair => $"{collectionId}/{pair.Path}", values, pair =>
        {
            var attribution = references.ItemAttribution(pair.Part.Identity.TweakId);
            if (attribution.Length == 0) attribution = "Unknown part record; raw identity is preserved exactly.";
            return new TableRow($"{collectionId}/{pair.Path}", values.ToDictionary(value => value.Key,
                value => value.Key == "item" ? (ViewNode)new SectionNode(value.Value(pair), [new TextNode(attribution)])
                    { Description = attribution } : new TextNode(value.Value(pair))));
        });
    }

    public QueryableCollection Metadata => _collection.Metadata;
    public QueryableTableNode View() => _collection.View();
    public QueryableCollectionPage Query(QueryableCollectionQuery query) => _collection.Query(query);
}
