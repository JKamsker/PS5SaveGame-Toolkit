using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Read-only community schema hints, matched by declaring class and field hashes.</summary>
internal sealed class SchemaReadouts(IReferenceContext context)
{
    internal const string FieldsCatalog = "pragmata-field-schema";
    internal const string EnumsCatalog = "pragmata-enum-members";
    private readonly IReferenceCatalog? _fields = context.Catalog(FieldsCatalog);
    private readonly IReferenceCatalog? _enums = context.Catalog(EnumsCatalog);

    public string Describe(SaveNode node, PragmataDocument document)
    {
        // Array elements inherit the declaration of the array, never their positional index.
        var field = node.Index >= 0 && node.Parent?.Type == FieldTags.Array ? node.Parent : node;
        if (field.Parent?.Type != FieldTags.Class) return "";
        var declaration = _fields?.Find($"{field.Parent.ClassHash:x8}/{field.Hash:x8}");
        if (declaration is null) return "";
        var declaredType = declaration.Attributes.GetValueOrDefault("declaredType");
        var suffix = declaration.Attributes.GetValueOrDefault("isArray") == "true" ? "[]" : "";
        var text = $"Inferred community declaration: {declaredType}{suffix}. {declaration.AttributionText} ";
        var storage = declaration.Attributes.GetValueOrDefault("storageType");
        if ((node.Type == FieldTags.Int32 && storage == "S32") || (node.Type == FieldTags.UInt32 && storage == "U32") ||
            (node.Type == FieldTags.Enum && node.Size == sizeof(int) && storage is "S32" or "U32"))
        {
            var value = document.Read(node).AsInteger();
            // The typed stream's enum reader is signed; the RSZ declaration can be uint32.
            // Normalize only the lookup key, preserving the saved value and its editor spec.
            if (storage == "U32" && value < 0) value += (Int128)uint.MaxValue + 1;
            var member = _enums?.Find(FormattableString.Invariant($"{declaredType}:{value}"));
            if (member is not null)
                text += $"Inferred enum label: {member.Name} (raw {document.Read(node)}). {member.AttributionText} ";
        }
        return text + "Schema/enum labels do not prove PS5 gameplay effects, legal bounds, completion or flag combinations.";
    }
}
