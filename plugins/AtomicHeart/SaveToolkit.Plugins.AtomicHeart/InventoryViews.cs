using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

internal delegate FieldNode InventoryBinding(string id, string label, FieldSpec spec, Func<FieldValue> get,
    Action<FieldValue>? set, Func<FieldValue, string?>? validate);

internal sealed class InventoryViews(InventoryState state, InventoryBinding bind)
{
    internal const string CatalogId = "items";

    internal void RegisterBindings()
    {
        foreach (var item in state.Items) _ = Fields(item);
    }

    internal IReadOnlyList<ViewNode> BuildView() => [
        new TextNode("Existing player/storage inventory slots only. Empty slots remain read-only; world loot is excluded. Amount limits use the original maximum in each serialized container, not a proven stack cap. " + InventoryState.WriteWarning) { Tone = Tone.Warning },
        new TextNode(state.Catalog is null ? "No item pack loaded; exact raw IDs are shown. Asset replacement requires the attributed pack." :
            $"Names: {state.Catalog.Dataset.Attribution.SourceUrl}. Exact object-name joins; PC-derived labels are inferred on PS5. No icons or limits are bundled."),
        InventoryPlanning.Action(),
        new InventoryCollection(state, this).View,
        // Keep the existing bounded inspection path until F12 traverses queryable fields for F1.
        SectionNode.Lazy("Slot details and original bytes", "inventory/0"),
    ];

    internal IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        var parts = lazyId.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var start) || start < 0 || (start != 0 && start >= state.Items.Count))
            throw new KeyNotFoundException(lazyId);
        var rows = state.Items.Skip(start).Take(AtomicHeartDocument.PageSize).Select(Row).ToArray();
        var nodes = new List<ViewNode> { new TableNode([
            new("container", "Container"), new("asset", "Item reference"), new("raw", "Saved object path"),
            new("category", "Category"), new("instance", "ItemId"), new("amount", "Amount"), new("action", "Bulk edit"),
            new("details", "Details"), new("span", "Original decoded span")], rows) };
        if (start + AtomicHeartDocument.PageSize < state.Items.Count)
            nodes.Add(SectionNode.Lazy("More slots", $"inventory/{start + AtomicHeartDocument.PageSize}"));
        return nodes;
    }

    internal TableRow Row(InventoryItem item)
    {
        var rawId = InventoryState.ShortId(state.Asset(item));
        var reference = state.Catalog?.Find(rawId);
        var (referenceField, amount) = Fields(item);
        return new TableRow(item.Key, new Dictionary<string, ViewNode> {
            ["container"] = new TextNode(item.Container), ["asset"] = referenceField, ["raw"] = new TextNode(state.Asset(item)),
            ["category"] = new TextNode(InventoryLabels.Category(item, state)),
            ["instance"] = new TextNode(item.InstanceId.ToString()), ["amount"] = amount,
            ["action"] = InventoryPlanning.Action(), ["span"] = new TextNode(RecordViews.Span(item.Element.Tag)),
            ["details"] = new SectionNode("Item details", [
                new TextNode(reference?.Name ?? InventoryLabels.Name(rawId)),
                new TextNode(reference?.Description ?? "Formatted asset identifier, not a confirmed localized game label. Category is inferred from the serialized container; raw ID is retained."),
                new TextNode(reference?.AttributionText ?? "Original toolkit identifier formatting; source is this save's explicit ItemDataAsset and container tags."),
                new TextNode($"Raw ID: {rawId}; instance: {item.InstanceId}. {InventoryState.WriteWarning}"),
                RecordViews.Hex(item.Element.Tag, "Original slot bytes"),
            ]) { Collapsed = true },
        });
    }

    private (FieldNode Reference, FieldNode Amount) Fields(InventoryItem item)
    {
        var rawId = InventoryState.ShortId(state.Asset(item));
        var reference = state.Catalog?.Find(rawId);
        var referenceField = bind(item.Key + "/asset", reference?.Name ?? rawId,
            new ReferenceSpec(CatalogId) { AllowCustom = false, Assess = candidate => state.Eligibility(item, candidate.Key) },
            () => FieldValue.Of(InventoryState.ShortId(state.Asset(item))),
            state.CanReplace(item) ? value => state.SetAsset(item, value) : null,
            value => state.ValidateAsset(item, value)) with {
            Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
            Description = $"Exact saved asset: {state.Asset(item)}. Original payload: {RecordViews.Span(item.Asset.Payload)}. " +
                "Replacement keeps Amount, ItemId, grid and all unknown state. Only same-category catalog paths observed in this save are accepted. " +
                InventoryState.WriteWarning + " " + reference?.AttributionText,
            Warning = InventoryState.WriteWarning,
        };
        var amount = bind(item.Key + "/amount", "Amount", new IntegerSpec(Math.Min(0, item.Amount), Math.Max(item.Amount, state.Limit(item))),
            () => state.Current(item.Quantity), state.CanSetAmount(item) ? value => state.Set(item.Quantity, value) : null,
            value => state.ValidateAmount(item, value)) with {
            Confidence = Confidence.Inferred,
            Description = $"Serialized {item.Quantity.Type} Amount; original {RecordViews.Span(item.Quantity.Payload)}. " +
                $"Based on explicit inventory tags; bounded to 0–{state.Limit(item)} (original container maximum). No dependent state is guessed. " + InventoryState.WriteWarning,
            Warning = InventoryState.WriteWarning,
        };
        return (referenceField, amount);
    }
}
