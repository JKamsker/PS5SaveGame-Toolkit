using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Projects eligible raw scalar values while retaining the bounded text fallback for wide strings.</summary>
internal static class GvasFieldReferenceFactory
{
    public static ViewNode Value(string id, GvasNode node)
    {
        if (node.Scalar is null) return Text(node.Note ?? string.Empty);
        var value = GvasScalarField.ToFieldValue(node.Scalar).ToString();
        if (node.Scalar.Kind == GvasScalarKind.String && value.Length > GvasPropertyCollection.CellLimit)
            return Text(value);
        return new FieldReferenceNode(id);
    }

    public static ViewNode Value(GvasPropertyEntry entry)
    {
        return Value(entry.Id, entry.Node);
    }

    private static TextNode Text(string value) =>
        new(GvasPropertyEntry.Clip(value, GvasPropertyCollection.CellLimit));
}
