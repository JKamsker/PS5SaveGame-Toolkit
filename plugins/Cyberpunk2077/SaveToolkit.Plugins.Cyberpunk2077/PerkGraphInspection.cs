using System.Globalization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>Stored modern allocation diagnostics, without fabricated prerequisite edges or costs.</summary>
internal sealed class PerkGraphInspection
{
    public const string Prefix = "perk-graph/";
    private const string DetailsTitle = "Stored perk details";
    private const string Explanation = "Saved modern perk groups and ranks only; legacy areas are excluded. " +
        "Unknown state means eligibility/effects are unverified, including groups with no stored ranks. " +
        "No prerequisite edges, rank limits, point costs or DLC entitlement are inferred. " +
        "Full rule data and populated PS5 allocations remain owner validation items (#31).";
    private readonly InspectionRow[] _rows;
    private readonly string? _warning;

    public PerkGraphInspection(IReadOnlyList<InspectionRow> rows, string? warning)
    {
        _rows = rows.Where(row => row.Values["kind"] is "Modern perk group" or "Modern perk")
            .OrderBy(row => row.Values["group"], StringComparer.Ordinal)
            .ThenBy(row => row.Values["kind"], StringComparer.Ordinal)
            .ThenBy(row => row.Values["name"], StringComparer.Ordinal).ToArray();
        _warning = warning;
        if (_rows.Select(SpatialDetails.PerkId).Distinct(StringComparer.Ordinal).Count() != _rows.Length)
        {
            _warning = "Ambiguous duplicate modern perk group or identifier; graph omitted. Inspect the stored table instead.";
            _rows = [];
        }
    }

    public IReadOnlyList<ViewNode> Build() => _warning is not null
        ? [new TextNode(_warning) { Tone = Tone.Warning }]
        : [new TextNode(Explanation), .. Enumerable.Range(0, (_rows.Length + SpatialDetails.PageSize - 1) / SpatialDetails.PageSize)
            .Select(page => SectionNode.Lazy($"Stored perk graph · page {page + 1}", Prefix + page.ToString(CultureInfo.InvariantCulture))),
            .. (_rows.Length == 0 ? new ViewNode[] { new TextNode("No modern perk groups or ranks were decoded; this does not establish locked or unavailable perks.") } : [])];

    public IReadOnlyList<ViewNode> Expand(string lazyId, Func<string, string, FieldSpec, string, FieldNode> readField)
    {
        if (!lazyId.StartsWith(Prefix, StringComparison.Ordinal) || !int.TryParse(lazyId.AsSpan(Prefix.Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page < 0 ||
            page >= (_rows.Length + SpatialDetails.PageSize - 1) / SpatialDetails.PageSize)
            throw new KeyNotFoundException($"Unknown perk graph page '{lazyId}'.");
        var rows = _rows.Skip(page * SpatialDetails.PageSize).Take(SpatialDetails.PageSize).ToArray();
        var graph = new GraphNode("Stored modern perks", rows.Select(row =>
            new GraphItem(SpatialDetails.PerkId(row), row.Values["name"], GraphState.Unknown)
            {
                Branch = row.Values["group"],
                Cost = row.Values["kind"] == "Modern perk" ? $"Stored rank {row.Values["rank"]}; point cost unknown" : "Group summary; cost unknown",
                Description = SpatialDetails.Summary(row.Values["status"] + " " + Explanation + " " + row.Attribution),
                Target = new ViewTarget(ViewTargetKind.Field, SpatialDetails.PerkId(row))
                    { Path = ["tab:Skills and perks", "section:" + lazyId, "section:" + DetailsTitle] },
            }).ToArray(), [], Explanation);
        SpatialViewValidation.Validate(graph);
        return [graph, new SectionNode(DetailsTitle, rows.Select(row =>
            (ViewNode)SpatialDetails.Field(SpatialDetails.PerkId(row), row, readField)).ToArray()) { Collapsed = true }];
    }
}
