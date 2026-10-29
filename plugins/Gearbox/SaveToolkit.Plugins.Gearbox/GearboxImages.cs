using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Artwork is optional, local to the pinned pack and attributed beside every image.</summary>
internal static class GearboxImages
{
    internal static ViewNode Cell(IReferenceContext references, ReferenceRecord? record, bool part = false)
    {
        if (record?.AssetId is not { } asset || references.AssetProvenance(asset) is not { } provenance)
            return new TextNode("Image unavailable");

        var label = part ? "Whole-item reference thumbnail; not individual part appearance" : record.Name;
        var source = !part && record.Source is { } url ? url + "; " : string.Empty;
        // Direct ImageNode cells render inline. Alt also supplies the UI's hover attribution;
        // resolving provenance registers the image dataset in the document's Sources panel.
        return new ImageNode(asset, label + ". " + source + provenance.Attribution.ToShortText());
    }
}
