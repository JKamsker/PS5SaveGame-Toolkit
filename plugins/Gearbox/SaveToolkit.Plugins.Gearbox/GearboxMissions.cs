using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Observed mission state only. No reward, branch repair or world-position inference.</summary>
internal sealed class GearboxMissions(GearboxDocument document, GearboxLazyViews lazy)
{
    internal const string CollectionId = "missions";
    private const int MapPageSize = 100;
    private static readonly QueryableColumn[] Columns = [new("playthrough", "Playthrough", IntegerSpec.Int32),
        new("mission", "Mission"), new("status", "Stored status"), new("asset", "Raw mission asset")];
    private IEnumerable<Mission> Records => document.Profile ? [] : document.Root.Children("mission_playthroughs_data")
        .SelectMany((playthrough, index) => playthrough.Children("mission_list").Select((message, ordinal) => new Mission(index, ordinal, message)));
    public QueryableCollection Collection => new(CollectionId, "Missions across all playthroughs", Columns, Records.Count());
    public IReadOnlyList<ViewNode> Build() => document.Profile ? [] :
    [new QueryableTableNode(CollectionId, Collection.Title, Columns, Collection.TotalCount)
        { Description = "Search/filter/sort covers every stored playthrough. A status alone does not establish reward or collectible ownership." },
        lazy.Register("missions/checklist", "Mission checklist and external maps", Maps)];

    public QueryableCollectionPage Query(QueryableCollectionQuery query) => GearboxQuery.Page(Collection, query, Records.Select(mission =>
    {
        var path = mission.Message.Text("mission_class_path");
        var record = document.World.Find(path.Split('.')[0].ToLowerInvariant());
        var values = new Dictionary<string, string> { ["playthrough"] = mission.Playthrough.ToString(CultureInfo.InvariantCulture),
            ["mission"] = record is null ? path : $"{record.Name} — {path}", ["status"] = Status(mission.Message.Integer("status")), ["asset"] = path };
        return new GearboxQueryRow(mission.Id, values, () => new(mission.Id, values.ToDictionary(pair => pair.Key,
            pair => pair.Key == "mission" ? (ViewNode)Label(mission) : new TextNode(pair.Value))));
    }));

    private FieldNode Label(Mission mission) => document.World.Label(mission.Id + "/label", "Mission", mission.Message.Text("mission_class_path"), mission: true);

    private IReadOnlyList<ViewNode> Maps() => Enumerable.Range(0, (Records.Count() + MapPageSize - 1) / MapPageSize)
        .Select(page => (ViewNode)lazy.Register("missions/checklist/page/" + page, $"Mission checklist {page * MapPageSize + 1} onwards",
            () => MapPage(page))).ToArray();

    private IReadOnlyList<ViewNode> MapPage(int pageIndex)
    {
        var page = Records.Skip(pageIndex * MapPageSize).Take(MapPageSize).ToArray();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var markers = page.Select(mission =>
        {
            var field = Label(mission);
            var path = mission.Message.Text("mission_class_path");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
            var occurrence = seen.GetValueOrDefault(hash);
            seen[hash] = occurrence + 1;
            return new MapMarker($"mission/{mission.Playthrough}/{hash}/{occurrence}", GearboxSpatialText.Limit(DisplayName(mission)),
                mission.Message.Integer("status") == 2 ? MapMarkerStatus.Found : MapMarkerStatus.Unknown, "Playthrough " + mission.Playthrough)
            {
                Confidence = Confidence.Inferred,
                Description = GearboxSpatialText.Limit(field.Description + " Stored status: " + Status(mission.Message.Integer("status")) +
                    ". Complete means the saved mission flag only; reward claims, branches and coordinates are not inferred."),
                Target = new(ViewTargetKind.Field, mission.Id + "/map-status")
                    { Path = ["tab:Progression", "section:missions/checklist", "section:missions/checklist/page/" + pageIndex] },
            };
        }).ToArray();
        return [new MapNode("Stored mission checklist", new(0, 0, 1, 1), markers,
            "External-map mission checklist without coordinates or art. Found denotes stored status Complete; all other statuses remain Unknown in the map legend.")
        {
            CoordinateSpace = "External maps; no coordinate transform established",
            ExternalSource = new(document.Root.Schema.Wonderlands ? "https://mapgenie.io/tiny-tinas-wonderlands" : "https://mapgenie.io/borderlands-3",
                "Open community maps", "Map Genie; linked only. No map images, marker positions or community descriptions imported; reuse terms unverified."),
        }, .. page.Select(mission => document.InspectionField(mission.Id + "/map-status", DisplayName(mission),
            Status(mission.Message.Integer("status")), Confidence.Inferred, Label(mission).Description))];
    }
    private string DisplayName(Mission mission)
    {
        var path = mission.Message.Text("mission_class_path");
        return document.World.Find(path.Split('.')[0].ToLowerInvariant()) is { } record ? record.Name + " — " + path : path;
    }
    private static string Status(long value) => value switch { 0 => "Not started", 1 => "Active", 2 => "Complete", 3 => "Failed", _ => "Unknown (" + value.ToString(CultureInfo.InvariantCulture) + ")" };
    private sealed record Mission(int Playthrough, int Ordinal, ProtoMessage Message)
    {
        public string Id => $"missions/{Playthrough}/{Ordinal}";
    }
}
