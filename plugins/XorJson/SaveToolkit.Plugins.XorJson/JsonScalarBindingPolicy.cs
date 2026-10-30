using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Creates one scalar policy shared by rendered fields and targeted metadata.</summary>
internal sealed record JsonScalarBinding(string Label, FieldSpec Spec, FieldPresentation Presentation,
    Func<FieldValue, string?> Validate);

internal static class JsonScalarBindingPolicy
{
    public static JsonScalarBinding Create(JsonValueToken node, IReferenceContext references,
        bool canEdit, string writeWarning)
    {
        var spec = node.Spec ?? throw new ArgumentException("The node is not scalar.", nameof(node));
        ReferenceRecord? record = null;
        var catalogId = CharacterViews.CatalogFor(node);
        if (catalogId is not null && references.Catalog(catalogId) is { } catalog &&
            node.Scalar.Kind == FieldValueKind.Text)
        {
            // Keep the explicit nullable/union contract after a scalar is edited into a
            // published catalog value; the rendered field only uses a picker for raw text.
            if (node.Current.Kind == FieldValueKind.Text)
                spec = new ReferenceSpec(catalogId) { AllowCustom = false, ReadOnly = !canEdit };
            record = catalog.Find(node.Scalar.AsString());
        }
        if (!canEdit && !spec.ReadOnly) spec = spec with { ReadOnly = true };

        return new JsonScalarBinding(
            node.Label.Length == 0 ? node.TypedPath : node.Label,
            spec,
            new FieldPresentation
            {
                Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
                Warning = node.Warning ?? writeWarning,
                Description = $"{node.TypedPath}; original UTF-8 token at 0x{node.Offset:X}." +
                    (record is null ? "" : $" {record.Name} ({record.Category}). {record.Description} " +
                        $"{record.AttributionText}. PC catalog; PS5 semantics unverified."),
            },
            value => JsonActions.Validate(node, value, references));
    }
}
