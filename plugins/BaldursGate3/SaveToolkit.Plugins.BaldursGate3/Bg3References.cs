using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed class Bg3References(IReferenceContext context)
{
    internal const string Templates = "bg3_templates";
    internal const string Origins = "bg3_origins";
    // The native build is not supplied. Never choose build-specific data by guessing a version.
    private readonly IReferenceCatalog? _templates = BuildAgnostic(context.Catalog(Templates));
    private readonly IReferenceCatalog? _origins = BuildAgnostic(context.Catalog(Origins));

    internal ReferenceRecord? Template(string rawGuid) => _templates?.Find(rawGuid);
    internal ReferenceRecord? Origin(string rawOrigin) => _origins?.Find(rawOrigin);

    internal static FieldNode Describe(FieldNode field, ReferenceRecord? record, string rawId) => field with
    {
        Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
        Description = record is null ? $"Unresolved raw identifier: {rawId}. No matching build-agnostic reference record."
            : $"{record.Description} Raw identifier: {rawId}. {record.AttributionText}. Pack {record.Dataset.PackId}; candidate label only, not a live identity/ownership proof.",
    };

    private static IReferenceCatalog? BuildAgnostic(IReferenceCatalog? catalog) =>
        catalog?.Pack.GameBuilds.Count == 0 ? catalog : null;
}
