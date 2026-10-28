using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class CatalogueInspection
{
    internal const string ActionId = "references/inspect/items";
    internal const string ParameterId = "item";

    internal static ActionNode Action(IReferenceCatalog catalog)
    {
        var first = catalog.Search(new ReferenceQuery { Limit = 1 }).Items.FirstOrDefault();
        if (first is null) throw new InvalidOperationException("The Atomic Heart item catalogue is empty.");

        return new ActionNode(ActionId, "Inspect catalogue item")
        {
            Mutates = false,
            Description = "Read-only inspection of the pinned, attributed item catalogue. It does not edit the save or resolve gameplay ownership.",
            Parameters = [new FieldNode(ParameterId, "Catalogue item", new ReferenceSpec(InventoryViews.CatalogId), FieldValue.Of(first.Key))],
        };
    }

    internal static ActionOutcome Inspect(IReferenceCatalog? catalog, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (catalog is null) throw new InvalidOperationException("No pinned Atomic Heart item catalogue is available.");
        if (parameters.Count != 1 || !parameters.TryGetValue(ParameterId, out var value) || value.Kind != FieldValueKind.Text)
            throw new ArgumentException("Choose one catalogue item.", nameof(parameters));

        var record = catalog.Find(value.AsString())
            ?? throw new ArgumentException("The selected catalogue item is not in the pinned catalogue.", nameof(parameters));
        return new ActionOutcome
        {
            Message = $"{record.Name} · {record.Key} · {record.Category ?? "Unknown category"}",
            Warnings = ["Catalogue metadata is attributed reference material; it does not prove PS5 ownership, availability or gameplay effects."],
        };
    }
}
