using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class FactInspection
{
    public const string CollectionId = "facts";
    private readonly InspectionCollection<FactRow> _collection;

    public FactInspection(IReadOnlyDictionary<int, IReadOnlyList<SaveFact>> tables, CyberpunkReferences references,
        Func<string, string, FieldSpec, string, FieldNode> readField, Func<string, uint, FieldNode> valueField)
    {
        var rows = tables.SelectMany(table => table.Value.Select((fact, index) => new FactRow(table.Key, index, fact))).ToArray();
        string Name(FactRow row)
        {
            var record = references.Fact(row.Fact.Hash);
            return record?.Attributes.GetValueOrDefault("canonicalName") ?? record?.Name ?? $"0x{row.Fact.Hash:X8}";
        }
        TableRow BuildRow(FactRow row)
        {
            var record = references.Fact(row.Fact.Hash);
            var canonical = record?.Attributes.GetValueOrDefault("canonicalName") ?? record?.Name;
            var attribution = record?.AttributionText ?? "No unambiguous, hash-verified fact identifier in the pinned pack.";
            var key = $"facts/{row.Table}/{row.Index}";
            return new TableRow(key, new Dictionary<string, ViewNode>
            {
                ["table"] = new TextNode(row.Table.ToString(CultureInfo.InvariantCulture)),
                ["hash"] = new TextNode($"0x{row.Fact.Hash:X8}") { Monospace = true },
                ["name"] = readField(key + "/name", "Fact identifier", new TextSpec(), Name(row)) with
                {
                    Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
                    Description = attribution,
                    Warning = canonical is not null && canonical != canonical.Trim()
                        ? "Canonical name has significant leading/trailing whitespace; the exact string is retained." : null,
                },
                ["value"] = valueField(key, row.Fact.Value) with { Confidence = Confidence.Unknown,
                    Description = attribution, Warning = "Raw uint32; a named/debug/absent fact does not establish quest completion or a safe edit." },
            });
        }

        _collection = new(new QueryableCollection(CollectionId, "Saved facts (read-only)", [
            new("table", "Table node", IntegerSpec.UInt32), new("hash", "FNV-1a/32 hash"),
            new("name", "Fact identifier"), new("value", "Raw value", IntegerSpec.UInt32)], rows.Length)
        {
            Description = "Search covers every saved fact, including canonical whitespace. Exact filters: table, hash, name, value. " +
                "Names are community identifiers, not localized quest descriptions. Semantics and completion remain unknown; all values are read-only.",
        }, rows, row => $"facts/{row.Table}/{row.Index}", new Dictionary<string, Func<FactRow, string>>
        {
            ["table"] = row => row.Table.ToString(CultureInfo.InvariantCulture),
            ["hash"] = row => $"0x{row.Fact.Hash:X8}",
            ["name"] = Name,
            ["value"] = row => row.Fact.Value.ToString(CultureInfo.InvariantCulture),
        }, BuildRow, row => row.Fact.Hash.ToString(CultureInfo.InvariantCulture));
    }

    public QueryableCollection Metadata => _collection.Metadata;
    public QueryableTableNode View() => _collection.View();
    public QueryableCollectionPage Query(QueryableCollectionQuery query) => _collection.Query(query);

    private sealed record FactRow(int Table, int Index, SaveFact Fact);
}
