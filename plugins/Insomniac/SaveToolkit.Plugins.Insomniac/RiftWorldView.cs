using System.Globalization;
using System.Numerics;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class RiftWorldView(DdlSections sections, Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
{
    internal IReadOnlyList<ViewNode> Build()
    {
        var result = new List<ViewNode>
        {
            new TextNode("World state is read-only. Stored graph/map identities are retained; planet, quest and collectible-location joins are not established. Award codenames do not prove PSN trophy status.") { Tone = Tone.Warning },
        };
        var maps = MapEntries(DdlSections.FogTag, "MapAreas");
        if (maps.Count > 0) result.Add(MapTable(maps));
        var graphs = MapEntries(DdlSections.ObjectivesTag, "GraphDataTable");
        if (graphs.Count > 0)
            result.Add(new TableNode([new("key", "Stored graph key"), new("fields", "State fields")],
                graphs.Select(entry => new TableRow(entry.Name, new Dictionary<string, ViewNode>
                {
                    ["key"] = new TextNode(entry.Name) { Monospace = true },
                    ["fields"] = new TextNode(string.Join(", ", entry.Objects.SelectMany(o => o.Fields.Keys))),
                })).ToArray()) { Title = "Objective graphs", Description = DdlSections.Attribution });
        if (sections.Roots.TryGetValue(DdlSections.AwardsTag, out var awards) && awards.TryGetValue("Awards", out var list))
            result.Add(Awards(list));
        if (sections.Roots.TryGetValue(RiftPlayerView.GoldBoltTag, out var gold) && gold.TryGetValue("CollectedBoltHashes", out var hashes))
            result.Add(new SectionNode("Collected gold bolt identities", hashes.Values.Select((value, index) =>
                (ViewNode)(bind($"world/gold-bolts/{value.AsUInt64():x8}/{index}", "Unresolved gold bolt hash",
                    IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }, value) with
                {
                    Confidence = Confidence.Verified,
                    Description = "Stored CollectedBoltHashes entry; no guide number or planet is assigned. " + DdlSections.Attribution,
                })).DefaultIfEmpty(new TextNode("No collected gold bolt hashes are stored.")).ToArray()));
        return result;
    }

    private IReadOnlyList<DdlField> MapEntries(uint tag, string name) =>
        sections.Roots.TryGetValue(tag, out var root) && root.TryGetValue(name, out var map)
            ? map.Objects.SelectMany(o => o.Fields.Values).ToArray() : [];

    private TableNode MapTable(IReadOnlyList<DdlField> entries) => new(
        [new("key", "Stored area key"), new("columns", "Columns"), new("rows", "Rows"),
         new("bits", "Set bits"), new("bytes", "Bitset bytes"), new("bounds", "Stored bounds")],
        entries.Select(entry =>
        {
            var fields = entry.Objects.Count == 1 ? entry.Objects[0].Fields : new Dictionary<string, DdlField>();
            var bits = fields.GetValueOrDefault("RevealedCellsBitSet");
            int? setBits = bits?.Kind == DdlValueKind.UInt8 ? bits.Values.Sum(v => BitOperations.PopCount(v.AsUInt64())) : null;
            var prefix = $"world/maps/{entry.Name}";
            return new TableRow(entry.Name, new Dictionary<string, ViewNode>
            {
                ["key"] = new TextNode(entry.Name) { Monospace = true },
                ["columns"] = Counter(prefix, fields.GetValueOrDefault("ColumnCount")),
                ["rows"] = Counter(prefix, fields.GetValueOrDefault("RowCount")),
                ["bits"] = setBits is { } count ? bind($"{prefix}/set-bits", "Set bits", IntegerSpec.UInt32, FieldValue.Of((long)count)) with
                {
                    Confidence = Confidence.Inferred,
                    Description = "Population count of stored bytes; padding bits are included. This is not a calibrated exploration percentage.",
                } : new TextNode("Unresolved layout"),
                ["bytes"] = new TextNode(bits?.Count.ToString(CultureInfo.InvariantCulture) ?? "Unresolved"),
                ["bounds"] = new TextNode(string.Join("; ", fields.Values
                    .Where(f => f.Name is "TopLeft" or "BotRight" or "CellDimensions")
                    .Select(f => $"{f.Name}: {Summary(f.Objects)}"))),
            });
        }).ToArray())
    {
        Title = "Fog-of-war areas",
        Description = "Stored dimensions and bitset counts; axes, bit order and world-to-map transforms remain unknown. " + DdlSections.Attribution,
    };

    private ViewNode Counter(string prefix, DdlField? field) =>
        field is { Values.Count: 1 } && field.Values[0].Kind == FieldValueKind.Integer
        ? bind($"{prefix}/{field.Name}", field.Name, field.Spec, field.Values[0]) with
        {
            Confidence = Confidence.Verified,
            Description = "Validated stored grid dimension. " + DdlSections.Attribution,
        } : new TextNode("Unresolved layout; inspect Systems");

    private static TableNode Awards(DdlField list) => new([new("code", "Stored codename"), new("state", "Save fields")],
        list.Objects.Select((award, index) => new TableRow(index.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>
        {
            ["code"] = new TextNode(award.Fields.TryGetValue("CodeName", out var code) ? code.Values.Single().AsString() : "Unresolved"),
            ["state"] = new TextNode(Summary(award.Fields.GetValueOrDefault("AwardSave")?.Objects ?? [])),
        })).ToArray())
    {
        Title = "Award records",
        Description = "Codenames are save content. Inspect Systems → Awards for raw progress values; no platform unlock claim is made. " + DdlSections.Attribution,
    };

    private static string Summary(IReadOnlyList<DdlObject> objects) => string.Join(", ", objects
        .SelectMany(o => o.Fields.Values).Where(f => f.Values.Count == 1).Take(12)
        .Select(f => $"{f.Name}={f.Values[0]}"));
}
