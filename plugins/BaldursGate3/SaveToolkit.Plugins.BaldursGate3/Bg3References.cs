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
    internal static IReadOnlyList<string> CatalogIds { get; } =
        [Templates, Items, Equipment, Spells, Passives, Origins, Companions, CampSupplies];
    // The native build is not supplied. Never choose build-specific data by guessing a version.
    private readonly IReferenceCatalog? _templates = BuildAgnostic(context.Catalog(Templates));
    private readonly IReferenceCatalog? _origins = BuildAgnostic(context.Catalog(Origins));
    private readonly IReadOnlyDictionary<string, IReferenceCatalog> _catalogs = CatalogIds
        .Select(id => (Id: id, Catalog: BuildAgnostic(context.Catalog(id))))
        .Where(pair => pair.Catalog is not null)
        .ToDictionary(pair => pair.Id, pair => pair.Catalog!, StringComparer.Ordinal);

    internal ReferenceRecord? Template(string rawGuid) => _templates?.Find(rawGuid);
    internal ReferenceRecord? Origin(string rawOrigin) => _origins?.Find(rawOrigin);

    internal IReadOnlyList<IReferenceCatalog> Catalogs =>
        CatalogIds.Where(id => _catalogs.ContainsKey(id)).Select(id => _catalogs[id]).ToArray();

    internal IReferenceCatalog? Catalog(string id) => _catalogs.GetValueOrDefault(id);

    internal static FieldNode Describe(FieldNode field, ReferenceRecord? record, string rawId) => field with
    {
        Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
        Description = record is null ? $"Unresolved raw identifier: {rawId}. No matching build-agnostic reference record."
            : $"{record.Description} Raw identifier: {rawId}. {record.AttributionText}. Pack {record.Dataset.PackId}; candidate label only, not a live identity/ownership proof.",
    };

    private static IReferenceCatalog? BuildAgnostic(IReferenceCatalog? catalog) =>
        catalog?.Pack.GameBuilds.Count == 0 ? catalog : null;
}
