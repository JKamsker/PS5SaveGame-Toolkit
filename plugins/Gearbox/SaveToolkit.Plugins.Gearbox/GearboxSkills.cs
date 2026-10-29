using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Keep named calculator facts separate from opaque save-subobject identities.</summary>
internal sealed class GearboxSkills(GearboxDocument document, GearboxLazyViews lazy, IReferenceContext references)
{
    internal const string CatalogId = "skills";
    internal const string AllocationsId = "skill-allocations";
    private const int GraphPageSize = 100;
    private const string ReferenceNotice = "Calculator names are joined to stored subobjects only when a pinned community tree prefix identifies the class/tree and the remaining fields are unambiguous. " +
        "Diagram edges describe cumulative tree-point gates, never a requirement to buy every earlier skill. No availability or ownership is inferred.";
    private static readonly QueryableColumn[] ReferenceColumns = [new("name", "Skill"), new("branch", "Class / tree"),
        new("tier", "Calculator tier", IntegerSpec.Int32), new("max", "Calculator ranks", IntegerSpec.Int32), new("description", "Description"),
        new("icon", "Skill icon") { Width = ColumnWidth.Narrow }];
    private static readonly QueryableColumn[] AllocationColumns = [new("asset", "Stored subobject"), new("tree", "Raw tree ID", IntegerSpec.Int32),
        new("class", "Community class"), new("treeName", "Community tree"), new("skill", "Joined skill"),
        new("points", "Stored points", IntegerSpec.Int32), new("max", "Stored max points", IntegerSpec.Int32), new("join", "Join confidence")];
    private readonly ReferenceRecord[] _records = SerialDatabase.For(document.Root.Schema.Wonderlands).Records(CatalogId).OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
    private readonly ReferenceRecord[] _crosswalk = SerialDatabase.For(document.Root.Schema.Wonderlands).Records(ItemCatalogs.SkillCrosswalk).ToArray();
    private readonly IReferenceCatalog? _catalog = references.Catalog(CatalogId);
    private readonly IReferenceCatalog? _crosswalkCatalog = references.Catalog(ItemCatalogs.SkillCrosswalk);
    private ReferenceRecord[] Records => _records;
    private ProtoMessage[] Allocations => document.Profile ? [] : document.Root.Child("ability_data")?.Children("tree_item_list").ToArray() ?? [];
    public IReadOnlyList<QueryableCollection> Collections => [new(CatalogId, "Named skill reference", ReferenceColumns, Records.Length),
        new(AllocationsId, "Stored skill allocations", AllocationColumns, Allocations.Length)];

    public IReadOnlyList<ViewNode> Build()
    {
        List<ViewNode> nodes = [new TextNode(ReferenceNotice) { Tone = Tone.Warning }];
        nodes.AddRange(Collections.Select(c => new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount)));
        if (!document.Profile) nodes.Add(lazy.Register("skills/stored", "Stored skill graphs", StoredGraphs));
        foreach (var branch in Records.GroupBy(r => r.Category ?? "Unknown"))
        {
            var records = branch.ToArray();
            var id = "skills/reference/" + Identity(branch.Key);
            nodes.Add(lazy.Register(id, branch.Key, () => ReferenceGraph(records, id)));
        }
        return nodes;
    }

    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query)
    {
        var collection = Collections.Single(c => c.Id == id);
        return GearboxQuery.Page(collection, query, id == CatalogId ? Records.Select(ReferenceRow) : AllocationRows());
    }

    private ReferenceRecord Resolve(ReferenceRecord pinned) => _catalog?.Find(pinned.Key) ?? pinned;

    private GearboxQueryRow ReferenceRow(ReferenceRecord pinned)
    {
        var record = Resolve(pinned);
        var values = new Dictionary<string, string> { ["name"] = record.Name, ["branch"] = pinned.Category ?? "Unknown",
            ["tier"] = pinned.Attributes["tier"], ["max"] = pinned.Attributes["maxPoints"], ["description"] = record.Description ?? "Unknown", ["icon"] = record.Name };
        return new(record.Key, values, () => new(record.Key, values.ToDictionary(p => p.Key, p => (ViewNode)(p.Key == "name"
            ? document.InspectionReference("skills/reference/" + record.Key, "Skill", record.Key, CatalogId, Confidence.Inferred,
                record.Name + ". " + record.AttributionText + " " + ReferenceNotice)
            : p.Key == "icon" ? GearboxImages.Cell(references, record) : new TextNode(p.Value)))));
    }

    private IEnumerable<GearboxQueryRow> AllocationRows() => Allocations.Select((m, index) =>
    {
        var path = m.Text("item_asset_path");
        var join = Join(path);
        var values = new Dictionary<string, string> { ["asset"] = path, ["tree"] = Number(m.Integer("tree_identifier")),
            ["class"] = join?.Attributes.GetValueOrDefault("className") ?? "Unknown", ["treeName"] = join?.Attributes.GetValueOrDefault("treeName") ?? "Unknown",
            ["skill"] = "Unknown skill (tree only)", ["points"] = Number(m.Integer("points")), ["max"] = Number(m.Integer("max_points")),
            ["join"] = join?.Attributes.GetValueOrDefault("joinConfidence") ?? "Unknown" };
        var id = "skills/stored/" + index;
        return new GearboxQueryRow(id, values, () => new(id, values.ToDictionary(p => p.Key, p => (ViewNode)document.InspectionField(id + "/" + p.Key,
            p.Key, p.Value, join is null ? Confidence.Unknown : Confidence.Inferred,
            SerialWrites.Attribution + " " + (join?.AttributionText ?? "No community tree-prefix match; stored path remains authoritative.")))));
    });

    private ReferenceRecord? Join(string path)
    {
        var pinned = _crosswalk.FirstOrDefault(r => path.StartsWith(r.Attributes["pathPrefix"], StringComparison.OrdinalIgnoreCase));
        return pinned is null ? null : _crosswalkCatalog?.Find(pinned.Key) ?? pinned;
    }

    private IReadOnlyList<ViewNode> StoredGraphs() => Enumerable.Range(0, (Allocations.Length + GraphPageSize - 1) / GraphPageSize)
        .Select(page => (ViewNode)lazy.Register("skills/stored/page/" + page, $"Stored allocations {page * GraphPageSize + 1}–{Math.Min((page + 1) * GraphPageSize, Allocations.Length)}",
            () => StoredPage(page))).ToArray();

    private IReadOnlyList<ViewNode> StoredPage(int page)
    {
        var items = new List<GraphItem>();
        var fields = new List<ViewNode>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (message, index) in Allocations.Select((m, i) => (m, i)).Skip(page * GraphPageSize).Take(GraphPageSize))
        {
            var path = message.Text("item_asset_path");
            var join = Join(path);
            var id = "skills/stored/" + index;
            var points = message.Integer("points");
            var hash = Identity(path);
            var occurrence = seen.GetValueOrDefault(hash);
            seen[hash] = occurrence + 1;
            fields.Add(document.InspectionField(id + "/points", "Stored points", Number(points), Confidence.Unknown,
                path + "; stored max " + Number(message.Integer("max_points")) + ". " + SerialWrites.Attribution));
            items.Add(new("stored/" + hash + "/" + occurrence,
                GearboxSpatialText.Limit(path.Split(':').LastOrDefault() is { Length: > 0 } leaf ? leaf : "Unnamed subobject"),
                points > 0 ? GraphState.Unlocked : GraphState.Unknown)
            {
                Branch = join?.Attributes.GetValueOrDefault("className") is { } className ? className + " / " + join.Attributes.GetValueOrDefault("treeName") : "Raw tree " + Number(message.Integer("tree_identifier")),
                Cost = $"{points} / {message.Integer("max_points")} stored points",
                Description = GearboxSpatialText.Limit(SerialWrites.Attribution + ". Unspent/zero does not prove locked or available. " +
                    (join?.Description ?? "No community tree-prefix match; skill name and prerequisites remain unknown.") + " " + path),
                Target = new(ViewTargetKind.Field, id + "/points") { Path = ["tab:Skills", "section:skills/stored", "section:skills/stored/page/" + page] },
            });
        }
        return [new GraphNode("Stored skill allocations", items, [],
            "Positive stored points are shown as unlocked. Opaque subobjects remain raw. No prerequisite edges have been inferred."), .. fields];
    }
    private IReadOnlyList<ViewNode> ReferenceGraph(ReferenceRecord[] records, string lazyId)
    {
        List<GraphItem> items = [];
        List<GraphEdge> edges = [];
        List<ViewNode> fields = [];
        foreach (var tier in records.GroupBy(r => r.Attributes["requiredPoints"]).OrderBy(g => int.Parse(g.Key, CultureInfo.InvariantCulture)))
        {
            var gate = "gate/" + tier.Key;
            var fieldId = lazyId + "/" + gate;
            fields.Add(document.InspectionField(fieldId, "Calculator tree-point gate", tier.Key, Confidence.Inferred, records[0].AttributionText + " " + ReferenceNotice));
            items.Add(new(gate, tier.Key + " points in earlier tiers", GraphState.Unknown) { Branch = records[0].Category,
                Description = ReferenceNotice + " " + records[0].AttributionText,
                Target = new(ViewTargetKind.Field, fieldId) { Path = ["tab:Skills", "section:" + lazyId] } });
            foreach (var pinned in tier)
            {
                var record = Resolve(pinned);
                var id = lazyId + "/" + record.Key;
                fields.Add(document.InspectionField(id, record.Name, record.Description ?? "Description unknown", Confidence.Inferred, record.AttributionText));
                fields.Add(GearboxImages.Cell(references, record));
                items.Add(new(record.Key, record.Name, GraphState.Unknown) { Branch = pinned.Category,
                    Cost = pinned.Attributes["maxPoints"] == "0" ? "Action / augment reference" : "Up to " + pinned.Attributes["maxPoints"] + " ranks (calculator)",
                    Description = GearboxSpatialText.Limit(record.AttributionText + " " + ReferenceNotice + " " + record.Description),
                    Target = new(ViewTargetKind.Field, id) { Path = ["tab:Skills", "section:" + lazyId] } });
                edges.Add(new(gate, record.Key));
            }
        }
        return [new GraphNode(records[0].Category ?? "Skill reference", items, edges, ReferenceNotice), .. fields];
    }

    private static string Identity(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
