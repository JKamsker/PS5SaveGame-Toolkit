using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroCollectionBrowser(
    AstroSchema schema,
    AstroLocations locations,
    Func<AstroField, FieldValue> current,
    Func<string, string, string, string, Confidence, FieldNode> label,
    Func<string, string, string, string, Confidence, FieldNode> reference,
    Func<long?> coins)
{
    public AstroCollectionQuery Queryable { get; } = new(schema, locations, current,
        record => Row(record, locations.Reference(record), current, label, locations));

    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Catalogue labels are read-only. Ownership means the saved IsGot flag; names and locations are candidates, not console-validated mappings. Search/filter to find missing records, then preview the ownership action before applying."),
        BuildSummary(),
        new SectionNode("All saved collectibles", [Queryable.View(), AstroPlanning.CollectionAction(), AstroRestorePlanning.View(), .. InspectionActions()]),
        Group("Artifact", "Artifacts"), Group("Puzzle", "Puzzle pieces"), Group("Prop", "Props"),
        .. AstroPlanning.Views(coins()),
        .. AstroWorldPlanning.Views(schema, locations),
        new TextNode(AstroWriteWarnings.Experimental + " " + AstroWriteWarnings.Collection + " " + AstroWriteWarnings.Integrity) { Tone = Tone.Warning }
    ];

    private SectionNode BuildSummary()
    {
        var records = schema.Records.Where(r => r.Group is "Artifact" or "Puzzle" or "Prop").ToArray();
        var owned = records.Count(r => r.Ownership is { } f && current(f).AsBool());
        var cinematic = records.Count(r => r.Cinematic is { } f && current(f).AsBool());
        var areas = records.GroupBy(r => locations.Reference(r)?.Category ?? "Unmapped area", StringComparer.Ordinal)
            .Select(group => new TableRow(group.Key, new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["area"] = new TextNode(group.Key),
                ["records"] = new TextNode(group.Count().ToString()),
                ["owned"] = new TextNode(group.Count(r => r.Ownership is { } f && current(f).AsBool()).ToString()),
                ["missing"] = new TextNode(group.Count(r => r.Ownership is { } f && !current(f).AsBool()).ToString()),
                ["confidence"] = new TextNode(group.Key == "Unmapped area" ? "Unknown" : "Inferred")
            })).ToArray();
        return new SectionNode("Candidate-area progress", [
            new StatsNode([
                new Stat("Saved collectibles owned", $"{owned}/{records.Length}"),
                new Stat("Saved cinematic flags true", cinematic.ToString()),
                new Stat("Missing ownership flags", records.Count(r => r.Ownership is { } f && !current(f).AsBool()).ToString()),
                new Stat("Unknown ownership", records.Count(r => r.Ownership is null).ToString())]),
            new TextNode("Area totals use attributed candidate world aliases. Puzzle rows are named only to their inferred internal stage group; no physical piece location is claimed."),
            new TableNode([
                new TableColumn("area", "Candidate area") { Width = ColumnWidth.Wide },
                new TableColumn("records", "Records"),
                new TableColumn("owned", "Owned"),
                new TableColumn("missing", "Missing"),
                new TableColumn("confidence", "Meaning")
            ], areas) { Title = "Existing-record totals" }
        ]);
    }

    private IReadOnlyList<ViewNode> InspectionActions()
    {
        var candidate = schema.Records.Select(record => (Record: record, Reference: locations.Reference(record)))
            .FirstOrDefault(item => item.Reference is not null);
        if (candidate.Reference is null)
        {
            return [];
        }

        var key = AstroLocations.Key(candidate.Record);
        var description = $"{candidate.Reference.Description} {candidate.Reference.AttributionText}";
        return [new ActionNode(AstroReferences.InspectActionId, "Inspect a catalogue record")
        {
            Mutates = false,
            Tone = Tone.Info,
            Description = "Read-only catalogue details. Saved ownership and gameplay effects are not established.",
            Parameters = [reference("record", "Catalogue record", key, description, Confidence.Inferred)]
        }];
    }

    private SectionNode Group(string group, string title)
    {
        var records = schema.Records.Where(r => r.Group == group).ToArray();
        var resolved = records.Select(r => (Record: r, Reference: locations.Reference(r))).ToArray();
        var mapped = resolved.Count(r => IsMapped(r.Reference));
        var acquired = records.Count(r => r.Ownership is { } f && current(f).AsBool());
        var unknown = records.Count(r => r.Ownership is null);
        return new SectionNode(title, records.Length == 0 ? [new TextNode($"No {group} records exist in this save.")] : [
            new TextNode(group == "Puzzle"
                ? "Names refer to inferred internal stage groups; physical piece locations are not proven. Internal prefix groups can split 5/3; they must not be relabeled as four pieces per stage."
                : "Mapped counts refer to candidate identity aliases; Inferred labels still require an owner catalogue check."),
            records.Length > AstroCollectionQuery.EagerLimit ? Queryable.GroupView(group) : new TableNode([
                new TableColumn("name", "Name / saved alias") { Width = ColumnWidth.Wide },
                // F3 names row controls from the first text cell. Put the unique raw key before
                // status/category so every details/selection control has an unambiguous label.
                new TableColumn("key", "Exact saved key") { Width = ColumnWidth.Narrow },
                new TableColumn("image", "Thumbnail") { Width = ColumnWidth.Narrow },
                new TableColumn("status", "Saved ownership") { Width = ColumnWidth.Narrow },
                new TableColumn("category", "Category / candidate world"),
                new TableColumn("world", "Candidate world"),
                new TableColumn("details", "Evidence and provenance") { Width = ColumnWidth.Narrow }
            ], resolved.Select(r => Row(r.Record, r.Reference, current, label, locations)).ToArray())
            { Title = title, Description = "Names include source attribution and confidence; expand a row for raw IDs and flag evidence." }
        ])
        {
            Badge = $"{acquired}/{records.Length} owned; {mapped} mapped / {records.Length - mapped} unmapped; {unknown} unknown ownership"
        };
    }

    private static TableRow Row(AstroCollectionRecord record, ReferenceRecord? reference,
        Func<AstroField, FieldValue> current,
        Func<string, string, string, string, Confidence, FieldNode> label, AstroLocations locations)
    {
        var key = $"{record.Group}/{record.Element.Name}";
        var mapped = IsMapped(reference);
        var confidence = mapped ? Confidence.Inferred : Confidence.Unknown;
        var provenance = reference is null ? "No matching schema-qualified reference; exact raw key retained."
            : $"{reference.Description} {reference.AttributionText}. Pack {reference.Dataset.PackId}.";
        var status = record.Ownership is { } owned ? current(owned).AsBool() ? "Owned" : "Missing" : "Unknown";
        var cinematic = record.Cinematic is { } field ? current(field).ToString() : "unknown or ambiguous";
        return new TableRow(record.Element.Id, new Dictionary<string, ViewNode>(StringComparer.Ordinal)
        {
            ["name"] = label($"catalogue:{record.Element.Id}", "Catalogue name", reference?.Name ?? key,
                $"Exact key: {key}. {provenance} No physical location or linked gameplay effect is proven.", confidence),
            ["status"] = new TextNode(status),
            ["category"] = new TextNode(reference?.Category ?? "Unmapped"),
            ["world"] = new TextNode(locations.World(record)?.Name ?? "Unmapped"),
            ["key"] = new TextNode(key) { Monospace = true },
            ["image"] = reference?.AssetId is { } asset
                ? new ImageNode(asset, $"{reference.Name} — candidate illustration. {provenance}")
                : new TextNode("No identified image"),
            ["details"] = new SectionNode("Record details", [
                new TextNode($"Stable saved record: {record.Element.Id}") { Monospace = true },
                new TextNode($"Saved IsGot: {status}. Saved IsCinematicsPlayed: {cinematic}. Flags are independent; neither implies the other."),
                new TextNode(reference?.Attributes.GetValueOrDefault("internalGroup") is { } prefix
                    ? $"Internal group: {prefix}. {MappingText(reference)}"
                    : reference?.Attributes.GetValueOrDefault("worldKey") is { } worldKey
                        ? $"Candidate world key: {worldKey}. Identity and pickup location remain Inferred."
                        : "No piece-location mapping supplied."),
                new TextNode(provenance),
                new TextNode("Select rows in this table, then review the selection below. An empty selection changes nothing. Individual experimental flag controls remain in Raw.") { Tone = Tone.Warning },
                AstroPlanning.CollectionAction(), AstroRestorePlanning.View()
            ])
        });
    }

    private static bool IsMapped(ReferenceRecord? record) =>
        record?.Attributes.GetValueOrDefault("mapping") is "candidate-identity" or "candidate-stage";

    private static string MappingText(ReferenceRecord reference) => reference.Attributes.GetValueOrDefault("mapping") switch
    {
        "candidate-stage" => "Candidate stage alias (Inferred); physical piece order/location remains unresolved.",
        "candidate-identity" => "Candidate identity alias (Inferred); pickup and linked effects remain unverified.",
        _ => "No verified mapping supplied."
    };
}
