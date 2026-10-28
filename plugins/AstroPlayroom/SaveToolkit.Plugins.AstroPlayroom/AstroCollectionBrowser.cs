using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroCollectionBrowser(
    AstroSchema schema,
    AstroReferences references,
    Func<AstroField, FieldValue> current,
    Func<string, string, string, string, Confidence, FieldNode> label)
{
    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Read-only catalogue. Ownership means the saved IsGot flag; names and locations are candidates, not console-validated mappings. Use table search and status/category filters to find missing records."),
        Group("Artifact", "Artifacts"), Group("Puzzle", "Puzzle pieces"), Group("Prop", "Props"),
        new TextNode("Collection actions are unavailable: controlled captures have not established ownership, cinematic, notification and UDS dependencies. No completion or trophy guarantee is made.") { Tone = Tone.Warning }
    ];

    private SectionNode Group(string group, string title)
    {
        var records = schema.Records.Where(r => r.Group == group).ToArray();
        var counts = records.GroupBy(r => r.Element.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var resolved = records.Select(r => (Record: r, Reference: counts[r.Element.Name] == 1
            ? references.Collectible(group, r.Element.Name) : null)).ToArray();
        var mapped = resolved.Count(r => IsMapped(r.Reference));
        var acquired = records.Count(r => r.Ownership is { } f && current(f).AsBool());
        var unknown = records.Count(r => r.Ownership is null);
        return new SectionNode(title, records.Length == 0 ? [new TextNode($"No {group} records exist in this save.")] : [
            new TextNode(group == "Puzzle"
                ? "Mapped counts refer to physical piece locations: none are proven. Internal prefix groups can split 5/3; they must not be relabeled as four pieces per stage."
                : "Mapped counts refer to candidate identity aliases; Inferred labels still require an owner catalogue check."),
            new TableNode([
                new TableColumn("name", "Name / saved alias") { Width = ColumnWidth.Wide },
                // F3 names row controls from the first text cell. Put the unique raw key before
                // status/category so every details/selection control has an unambiguous label.
                new TableColumn("key", "Exact saved key") { Width = ColumnWidth.Narrow },
                new TableColumn("status", "Saved ownership") { Width = ColumnWidth.Narrow },
                new TableColumn("category", "Category / candidate world"),
                new TableColumn("details", "Evidence and provenance") { Width = ColumnWidth.Narrow }
            ], resolved.Select(r => Row(r.Record, r.Reference)).ToArray())
            { Title = title, Description = "Names include source attribution and confidence; expand a row for raw IDs and flag evidence." }
        ])
        {
            Badge = $"{acquired}/{records.Length} owned; {mapped} mapped / {records.Length - mapped} unmapped; {unknown} unknown ownership"
        };
    }

    private TableRow Row(AstroCollectionRecord record, ReferenceRecord? reference)
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
            ["key"] = new TextNode(key) { Monospace = true },
            ["details"] = new SectionNode("Record details", [
                new TextNode($"Stable saved record: {record.Element.Id}") { Monospace = true },
                new TextNode($"Saved IsGot: {status}. Saved IsCinematicsPlayed: {cinematic}. Flags are independent; neither implies the other."),
                new TextNode(reference?.Attributes.GetValueOrDefault("internalGroup") is { } prefix
                    ? $"Internal group: {prefix}. Physical piece order/location is unresolved." : "No piece-location mapping supplied."),
                new TextNode(provenance),
                new TextNode("This view cannot grant/revoke items. Existing experimental flag controls are in Raw; changing a flag does not validate rewards, cinematics, notifications or trophies.") { Tone = Tone.Warning }
            ])
        });
    }

    private static bool IsMapped(ReferenceRecord? record) =>
        record?.Attributes.GetValueOrDefault("mapping") == "candidate-identity";
}
