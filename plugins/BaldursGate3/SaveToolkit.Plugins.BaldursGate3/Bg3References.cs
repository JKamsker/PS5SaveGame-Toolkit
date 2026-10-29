using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed class Bg3References(IReferenceContext context)
{
    internal const string Templates = "bg3_templates";
    internal const string Origins = "bg3_origins";
    internal const string Items = "bg3_items";
    internal const string Equipment = "bg3_equipment";
    internal const string Spells = "bg3_spells";
    internal const string Passives = "bg3_passives";
    internal const string Companions = "bg3_companions";
    internal const string CampSupplies = "bg3_camp_supplies";
    internal const string Classes = "bg3_classes";
    internal static IReadOnlyList<string> CatalogIds { get; } =
        [Templates, Items, Equipment, Spells, Passives, Origins, Companions, CampSupplies, Classes];
    // The native build is not supplied. Never choose build-specific data by guessing a version.
    private readonly IReadOnlyDictionary<string, IReferenceCatalog> _catalogs = CatalogIds
        .Select(id => (Id: id, Catalog: context.Catalog(id)))
        .Where(pair => pair.Catalog?.Pack.GameBuilds.Count == 0)
        .ToDictionary(pair => pair.Id, pair => pair.Catalog!, StringComparer.Ordinal);

    internal ReferenceRecord? Template(string rawGuid) => Catalog(Templates)?.Find(rawGuid);
    internal ReferenceRecord? Origin(string rawOrigin) => Catalog(Origins)?.Find(rawOrigin);

    internal IReadOnlyList<IReferenceCatalog> Catalogs => CatalogIds.Select(Catalog).OfType<IReferenceCatalog>().ToArray();

    internal IReferenceCatalog? Catalog(string id) => _catalogs.GetValueOrDefault(id);

    internal ViewNode Icon(ReferenceRecord? record) =>
        ReferenceViews.Icon(context, record, label: null, fallback: null,
            options: new() { Availability = ReferenceIconAvailability.RequireProvenance });

    internal static FieldNode Describe(FieldNode field, ReferenceRecord? record, string rawId) => field with
    {
        Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
        Description = record is null ? $"Unresolved raw identifier: {rawId}. No matching build-agnostic reference record."
            : $"{record.Description} Raw identifier: {rawId}. {record.AttributionText}. Pack {record.Dataset.PackId}; candidate label only, not a live identity/ownership proof.",
    };
}
