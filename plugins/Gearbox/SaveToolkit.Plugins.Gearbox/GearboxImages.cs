using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Artwork is optional, local to the pinned pack and attributed beside every image.</summary>
internal static class GearboxImages
{
    internal static ViewNode Cell(IReferenceContext references, ReferenceRecord? record, bool part = false)
        => ReferenceViews.Icon(references, record,
            part ? "Whole-item reference thumbnail; not individual part appearance" : null,
            new TextNode("Image unavailable"));
}
