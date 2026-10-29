using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class CatalogueInspection
{
    internal const string ActionId = "references/inspect/items";
    internal const string ParameterId = "item";
    private const string Description = "Read-only inspection of the pinned, attributed item catalogue. It does not edit the save or resolve gameplay ownership.";

    internal static ActionNode Action(IReferenceCatalog catalog) =>
        (ReferenceInspection.Create(catalog, ActionId, "Inspect catalogue item", ParameterId, "Catalogue item", Description)
            ?? throw new InvalidOperationException("The Atomic Heart item catalogue is empty.")) with {
            Description = Description, Tone = Tone.Normal,
        };

    internal static ActionOutcome Inspect(IReferenceCatalog? catalog, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (catalog is null) throw new InvalidOperationException("No pinned Atomic Heart item catalogue is available.");
        if (parameters.Count != 1 || !parameters.TryGetValue(ParameterId, out var value) || value.Kind != FieldValueKind.Text)
            throw new ArgumentException("Choose one catalogue item.", nameof(parameters));

        ReferenceRecord record;
        try { record = ReferenceInspection.ResolveSelection(catalog, parameters, ParameterId); }
        catch (ArgumentException) { throw new ArgumentException("The selected catalogue item is not in the pinned catalogue.", nameof(parameters)); }
        return new ActionOutcome
        {
            Message = $"{record.Name} · {record.Key} · {record.Category ?? "Unknown category"}\n" +
                $"{record.Description}\n{ReferencePresentation.Attributes(record)}\n{record.AttributionText}\nInspection only; the save was not changed.",
            Warnings = ["Catalogue metadata is attributed reference material; it does not prove PS5 ownership, availability or gameplay effects."],
        };
    }
}
