using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed class InventoryViews
{
    internal const string CatalogId = "items";
    private sealed record Item(string Key, string Container, TaggedProperty Element,
        string RawAsset, long InstanceId, long Amount, TaggedProperty Asset, TaggedProperty Quantity);
    private readonly IReferenceCatalog? catalog;
    private readonly Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind;
    private readonly List<Item> items = [];
    private readonly HashSet<string> itemKeys = new(StringComparer.Ordinal);

    internal InventoryViews(RecordIndex index, IReferenceCatalog? catalog,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind)
    {
        this.catalog = catalog;
        this.bind = bind;
        foreach (var record in index.Objects.Where(r => r.Kind == RecordKinds.Component &&
            (r.ClassPath.EndsWith(InventoryClasses.Player, StringComparison.Ordinal) ||
             r.ClassPath.EndsWith(InventoryClasses.Storage, StringComparison.Ordinal))))
        {
            foreach (var property in record.Properties)
            {
                var entries = property.Children.FirstOrDefault(p => p.Name is "Slots" or "Elements");
                if (entries is null) continue;
                var container = record.Name + "/" + property.Name;
                foreach (var element in entries.Children)
                {
                    var asset = element.Children.FirstOrDefault(p => p.Name == "ItemDataAsset");
                    var quantity = element.Children.FirstOrDefault(p => p.Name == "Amount");
                    var instance = element.Children.FirstOrDefault(p => p.Name == "ItemId");
                    if (asset?.Value is not { Kind: FieldValueKind.Text } text ||
                        quantity?.Value is not { Kind: FieldValueKind.Integer } amount ||
                        instance?.Value is not { Kind: FieldValueKind.Integer } instanceId) continue;
                    // Empty slots retain distinct identities; never conflate zero with absent records.
                    var key = $"{record.Id}/{property.Name}/item/{instanceId.AsInt64()}";
                    if (!itemKeys.Add(key))
                    {
                        key += $"/slot/{element.Name}";
                        if (!itemKeys.Add(key)) throw new Sdk.SaveFormatException("Duplicate inventory slot identity.");
                    }
                    items.Add(new(key, container, element, text.AsString(), instanceId.AsInt64(), amount.AsInt64(), asset, quantity));
                }
            }
        }
    }

    internal IReadOnlyList<ViewNode> BuildView() => [
        new TextNode("Observed player inventory component records. Empty slots and unknown IDs remain visible. Amount and ItemId are serialized values; stack limits, equipped effects, ownership semantics and console behavior remain unverified. World loot is excluded from this view."),
        new TextNode(catalog is null ? "No item pack loaded; exact raw IDs are shown." :
            $"Names: {catalog.Dataset.Attribution.SourceUrl}. Name joins use the exact object asset name. PC-derived labels are inferred on PS5; no icons or limits are bundled."),
        SectionNode.Lazy($"Serialized inventory slots ({items.Count})", "inventory/0"),
    ];

    internal IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        var parts = lazyId.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var start) || start < 0 || (start != 0 && start >= items.Count))
            throw new KeyNotFoundException(lazyId);
        var rows = items.Skip(start).Take(AtomicHeartDocument.PageSize).Select(Row).ToArray();
        var nodes = new List<ViewNode> { new TableNode([
            new("container", "Container"), new("asset", "Item reference"), new("raw", "Saved object path"),
            new("instance", "ItemId"), new("amount", "Amount"), new("span", "Decoded span")], rows) };
        if (start + AtomicHeartDocument.PageSize < items.Count)
            nodes.Add(SectionNode.Lazy("More slots", $"inventory/{start + AtomicHeartDocument.PageSize}"));
        return nodes;
    }

    private TableRow Row(Item item)
    {
        var rawId = item.RawAsset[(item.RawAsset.LastIndexOf('.') + 1)..];
        var reference = catalog?.Find(rawId);
        var referenceField = bind(item.Key + "/asset", reference?.Name ?? rawId,
            new ReferenceSpec(CatalogId) { AllowCustom = true }, () => FieldValue.Of(rawId)) with {
            Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
            Description = $"Exact saved asset: {item.RawAsset}. Payload: {RecordViews.Span(item.Asset.Payload)}. " + reference?.AttributionText,
            Warning = "Read-only reference. A label match does not prove replacement compatibility or ownership.",
        };
        var amount = bind(item.Key + "/amount", "Amount", IntegerSpec.Int64, () => FieldValue.Of(item.Amount)) with {
            Confidence = Confidence.Verified, Description = $"Serialized Amount; {RecordViews.Span(item.Quantity.Payload)}.",
            Warning = "Gameplay effect and safe limits need controlled PS5 captures.",
        };
        return new TableRow(item.Key, new Dictionary<string, ViewNode> {
            ["container"] = new TextNode(item.Container), ["asset"] = referenceField, ["raw"] = new TextNode(item.RawAsset),
            ["instance"] = new TextNode(item.InstanceId.ToString()), ["amount"] = amount,
            ["span"] = new TextNode(RecordViews.Span(item.Element.Tag)),
        });
    }
}
