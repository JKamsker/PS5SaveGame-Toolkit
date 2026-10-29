using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

internal static class JsonActions
{
    public const string Bulk = "json/bulk-set";
    public const string Attribute = "character/set-attribute";
    public const string Stacks = "character/set-stacks";
    public const string Item = "character/replace-item";
    public const string Rarity = "character/set-rarity";
    public const int MaxChanges = 5000;

    public static bool IsPlanned(string id) => id is Bulk or Attribute or Stacks or Item or Rarity;

    public static bool TryFind(JsonEditModel model, string id, out JsonValueToken node)
    {
        node = null!;
        if (!id.StartsWith("value/", StringComparison.Ordinal) ||
            !int.TryParse(id.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return false;
        try { node = model.Find(index); return true; }
        catch (KeyNotFoundException) { return false; }
    }

    public static string? Validate(JsonValueToken node, FieldValue value, IReferenceContext references)
    {
        if (node.Validate(value) is { } error) return error;
        if (value == node.Original || value == node.Current) return null;
        var inner = value.Kind == FieldValueKind.Union ? value.AsUnion().Value : value;
        if (inner.Kind != FieldValueKind.Text) return null;
        var catalog = CharacterViews.CatalogFor(node) ?? CharacterViews.CatalogFor(node, inner);
        if (catalog is null) return null;
        return references.Catalog(catalog) is { } records &&
            records.Find(inner.AsString()) is null
            ? "Choose an identifier from the attributed catalog. Existing unknown original IDs are preserved." : null;
    }
}
