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
            $"Names and art: {state.Catalog.Dataset.Attribution.ToShortText()}. Exact object-name joins are inferred on PS5; recipes are reference information, not validated caps."),
        state.Catalog is null ? new TextNode("No pinned item catalogue is available for read-only inspection.") : CatalogueInspection.Action(state.Catalog),
        InventoryPlanning.Action(),
        new InventoryCollection(state, this).View,
    ];

    internal TableRow Row(InventoryItem item)
    {
        var rawId = InventoryState.ShortId(state.Asset(item));
        var reference = state.Catalog?.Find(rawId);
        var (referenceField, amount) = Fields(item);
        return new TableRow(item.Key, new Dictionary<string, ViewNode> {
            ["icon"] = ReferencePresentation.Icon(reference),
            ["container"] = new TextNode(item.Container), ["asset"] = referenceField, ["raw"] = new TextNode(state.Asset(item)),
            ["category"] = new TextNode(InventoryLabels.Category(item, state)),
            ["instance"] = new TextNode(item.InstanceId.ToString()), ["amount"] = amount,
            ["action"] = InventoryPlanning.Action(), ["span"] = new TextNode(RecordViews.Span(item.Element.Tag)),
            ["details"] = new SectionNode("Item details", [
                new TextNode(reference?.Name ?? InventoryLabels.Name(rawId)),
                new TextNode(reference?.Description ?? "Formatted asset identifier, not a confirmed localized game label. Category is inferred from the serialized container; raw ID is retained."),
                new TextNode(ReferencePresentation.Attributes(reference)),
                new TextNode(reference?.AttributionText ?? "Original toolkit identifier formatting; source is this save's explicit ItemDataAsset and container tags."),
                new TextNode($"Raw ID: {rawId}; instance: {item.InstanceId}. {InventoryState.WriteWarning}"),
                RecordViews.Hex(item.Element.Tag, "Original slot bytes"),
            ]) { Collapsed = true },
        }) { IsEmpty = state.IsEmpty(item) };
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
                InventoryState.WriteWarning + " " + reference?.Description + " " + reference?.AttributionText,
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
