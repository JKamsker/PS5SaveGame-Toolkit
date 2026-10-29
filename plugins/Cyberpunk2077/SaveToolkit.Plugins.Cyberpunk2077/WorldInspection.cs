using System.Globalization;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record TravelPoint(ulong Record, string Marker);

internal sealed class WorldInspection
{
    public const string VehiclesId = "vehicles";
    public const string TravelId = "travel";
    public const string RegisteredTravelState = "Registered in saved FastTravelSystem";
    private readonly TravelMapInspection _map;
    public WorldInspection(ScriptableReader? reader, CsavContainer container, CsavTree tree, CyberpunkReferences references, string? warning)
    {
        IReadOnlyList<GarageVehicle> garage = [];
        IReadOnlyList<TravelPoint> travel = [];
        string? garageWarning = warning;
        string? travelWarning = warning;
        if (reader is not null)
        {
            try { garage = GarageReader.Read(container, tree); }
            catch (SaveFormatException ex) { garageWarning = ex.Message; }
            try { travel = ReadTravel(reader); }
            catch (SaveFormatException ex) { travelWarning = ex.Message; }
        }
        GarageWarning = garageWarning;
        TravelWarning = travelWarning;
        var garageByRecord = garage.ToLookup(vehicle => vehicle.Record);
        var travelByRecord = travel.ToLookup(point => point.Record);
        var vehicles = CatalogRows(references.Catalog("vehicles"), garage.Select(v => v.Record))
            .Select(pair =>
            {
                var lists = garageByRecord[pair.Id].Select(v => v.List).Distinct(StringComparer.Ordinal).ToArray();
                return Row("vehicles/" + pair.Id, pair.Id, pair.Record, garageWarning is not null ? "Unknown: garage unavailable"
                    : lists.Length == 0 ? "Not listed in decoded garage" : string.Join(", ", lists),
                    "Stored garage list membership only; ownership, summon availability, entitlement and current spawned state are distinct.", GarageReader.Source);
            }).ToArray();
        Vehicles = new InspectionTable(VehiclesId, "Garage catalog and stored lists (read-only)", [new("id", "TweakDB ID"),
            new("name", "Vehicle identifier"), new("state", "Stored list membership"), new("details", "Evidence")], vehicles,
            "Catalog + saved garage union. No matching entry means not listed, not proven unavailable. Omitted garage arrays use empty defaults. " +
            "An unfamiliar garage property disables status interpretation rather than guessing around opaque bytes.");
        var points = CatalogRows(references.Catalog("travel"), travel.Select(v => v.Record)).Select(pair =>
        {
            var saved = travelByRecord[pair.Id].ToArray();
            var marker = pair.Record?.Attributes.GetValueOrDefault("markerRef");
            return Row("travel/" + pair.Id, pair.Id, pair.Record, travelWarning is not null ? "Unknown: travel unavailable"
                : saved.Length == 0 ? "Not registered in saved FastTravelSystem" : RegisteredTravelState,
                $"Saved marker: {string.Join(", ", saved.Select(p => p.Marker))}; catalog marker: {marker ?? "unknown"}; " +
                $"catalog IsEP1: {pair.Record?.Attributes.GetValueOrDefault("requiresEp1") ?? "unknown"}. " +
                "Registration does not prove current accessibility, discovery cause or DLC entitlement.");
        }).ToArray();
        _map = new TravelMapInspection(points, references);
        Travel = new InspectionTable(TravelId, "Fast travel registration (read-only)", [new("id", "TweakDB ID"),
            new("name", "Travel point identifier"), new("state", "Saved registration"), new("details", "Marker / evidence")], points,
            "Catalog + saved handle union; each handle must target gameFastTravelPointData. Marker refs remain separate from record IDs. " +
            "No unlock actions, completion percentage, invented coordinates or discovery claims.");
    }
    public string? GarageWarning { get; }
    public string? TravelWarning { get; }
    public InspectionTable Vehicles { get; }
    public InspectionTable Travel { get; }
    public IReadOnlyList<ViewNode> Build() => [new TextNode(GarageWarning ?? Vehicles.Metadata.Description!)
        { Tone = GarageWarning is null ? Tone.Info : Tone.Warning }, Vehicles.View(),
        new TextNode(TravelWarning ?? Travel.Metadata.Description!) { Tone = TravelWarning is null ? Tone.Info : Tone.Warning }, .. _map.Build(), Travel.View()];

    public IReadOnlyList<ViewNode> Expand(string id, Func<string, string, FieldSpec, string, FieldNode> readField) => _map.Expand(id, readField);

    private static IReadOnlyList<TravelPoint> ReadTravel(ScriptableReader reader)
    {
        var system = reader.System("FastTravelSystem");
        var points = reader.Handles(system, "fastTravelNodes", "gameFastTravelPointData");
        return points.Select(point =>
        {
            var id = ScriptableReader.UInt64(point, "pointRecord", "TweakDBID");
            var marker = "";
            if (point.Find("markerRef") is not null)
            {
                var bytes = point.Raw(point.Require("markerRef", "NodeRef"));
                var length = RedPackage.UInt16(bytes, 0);
                if (length != bytes.Length - 2 || length > 1024) throw new SaveFormatException("Invalid travel marker reference length.");
                try { marker = new UTF8Encoding(false, true).GetString(bytes, 2, length); }
                catch (DecoderFallbackException) { throw new SaveFormatException("Invalid travel marker encoding."); }
            }
            return new TravelPoint(id, marker);
        }).ToArray();
    }

    private static InspectionRow Row(string key, ulong id, ReferenceRecord? record, string state, string detail, string source = ScriptableReader.Source) => new(key,
        new Dictionary<string, string> { ["id"] = $"0x{id:X16}", ["name"] = record?.Name ?? $"0x{id:X16}", ["state"] = state, ["details"] = detail },
        source + (record is null ? "\nUnknown catalog identifier." : "\n" + record.AttributionText),
        record is null ? Confidence.Unknown : Confidence.Inferred);

    private static IEnumerable<(ulong Id, ReferenceRecord? Record)> CatalogRows(IReferenceCatalog? catalog, IEnumerable<ulong> saved)
    {
        var seen = new HashSet<ulong>();
        foreach (var record in CyberpunkReferences.All(catalog))
        {
            if (!ulong.TryParse(record.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                ItemNames.TweakHash(record.Attributes.GetValueOrDefault("internalName") ?? record.Name) != id) continue;
            if (seen.Add(id)) yield return (id, record);
        }
        foreach (var id in saved) if (seen.Add(id)) yield return (id, catalog?.Find(CyberpunkReferences.ItemKey(id)));
    }
}
