using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>Keep the merged index compact; dictionaries, provenance and bindings belong to a page.</summary>
internal sealed class FactCatalogInspection
{
    public const string CollectionId = "fact-catalog";
    private const string Evidence = "Exact canonical FNV-1a/32 join only. ";
    private const string Interpretation = "Absence is not zero; raw values, debug flags and prefixes do not establish active quest stage or completion.";
    private readonly InspectionCollection<FactRow> _collection;

    private FactCatalogInspection(IReadOnlyList<FactRow> rows, Func<int, int, uint>? currentValue,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode>? bindRead)
    {
        var nullableValue = new NullableSpec(IntegerSpec.UInt32) { ReadOnly = true };
        QueryableColumn[] columns = [new("hash", "FNV-1a/32"), new("name", "Exact canonical identifier"), new("group", "Identifier prefix (not a quest title)"),
            new("presence", "Presence"), new("value", "Raw uint32 / absent", nullableValue), new("table", "Table node", IntegerSpec.UInt32),
            new("evidence", "Interpretation")];
        FieldValue ReadValue(FactRow row) => row.Table is { } table
            ? FieldValue.Of((ulong)(currentValue?.Invoke(table, row.Index) ?? row.Value)) : FieldValue.Null;
        string Current(FactRow row) => row.Table is null ? "" : ReadValue(row).ToString();
        string Value(FactRow row, string column) => column switch
        {
            "hash" => row.Hash,
            "name" => row.Name,
            "group" => row.Group,
            "presence" => row.Table is null ? "Catalog only / absent from save" : "Present in save",
            "value" => Current(row),
            "table" => row.Table?.ToString(CultureInfo.InvariantCulture) ?? "",
            "evidence" => Evidence + (row.Reference is not null && row.Name != row.Name.Trim()
                ? "Significant surrounding whitespace retained. " : "") + Interpretation,
            _ => "",
        };
        TableRow BuildRow(FactRow row)
        {
            var attribution = row.Reference?.AttributionText ?? "Unknown saved hash; no unambiguous canonical match.";
            var confidence = row.Reference is null ? Confidence.Unknown : Confidence.Inferred;
            var cells = columns.ToDictionary(column => column.Key, column => (ViewNode)new SectionNode(
                Value(row, column.Key), [new TextNode(attribution)]) { Description = attribution, Badge = confidence.ToString() });
            if (bindRead is not null)
            {
                var field = bindRead(row.Key + "/value", "Saved fact value (null = absent)", nullableValue, () => ReadValue(row)) with
                {
                    Confidence = Confidence.Unknown,
                    Description = attribution + "\nAbsent catalog facts have no saved value; zero is a stored value. Edit existing values in Saved facts.",
                };
                cells["value"] = new SectionNode(Current(row), [field, new TextNode(attribution)])
                    { Description = field.Description, Badge = confidence.ToString() };
            }
            return new TableRow(row.Key, cells);
        }
        _collection = new(new QueryableCollection(CollectionId, "Quest facts: merged catalog and save (read-only)", columns, rows.Count)
        {
            Description = "F8 search/filter/sort covers the complete merged facts catalog plus every saved entry, including unknown hashes and repeated entries. " +
                "Use prefix, presence and raw-value filters to inspect progress evidence. Catalog-only rows have no stored value; no completion recipe. " +
                "Existing raw values can be edited in Saved facts with warnings.",
        }, rows, row => row.Key, columns.ToDictionary(column => column.Key,
            column => (Func<FactRow, string>)(row => Value(row, column.Key))), BuildRow);
    }

    public QueryableCollection Metadata => _collection.Metadata;
    public QueryableTableNode View() => _collection.View();
    public QueryableCollectionPage Query(QueryableCollectionQuery query) => _collection.Query(query);

    public static FactCatalogInspection Build(IReadOnlyDictionary<int, IReadOnlyList<SaveFact>> tables, CyberpunkReferences references,
        Func<int, int, uint>? currentValue = null,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode>? bindRead = null)
    {
        var rows = new List<FactRow>();
        var saved = new HashSet<uint>();
        void Add(uint hash, string key, ReferenceRecord? record, int? table = null, int index = 0, uint value = 0)
        {
            var canonical = record?.Attributes.GetValueOrDefault("canonicalName") ?? record?.Name;
            var name = canonical ?? $"0x{hash:X8}";
            var separator = name.IndexOf('_');
            var group = canonical is null ? "Unknown" : separator > 0 ? name[..separator] : "Other identifiers";
            rows.Add(new FactRow(key, $"0x{hash:X8}", name, group, record, table, index, value));
        }
        foreach (var table in tables)
        {
            for (var i = 0; i < table.Value.Count; i++)
            {
                var fact = table.Value[i];
                saved.Add(fact.Hash);
                Add(fact.Hash, $"fact-catalog/saved/{table.Key}/{i}", references.Fact(fact.Hash), table.Key, i, fact.Value);
            }
        }
        foreach (var record in CyberpunkReferences.All(references.Facts))
        {
            if (!uint.TryParse(record.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var hash) || saved.Contains(hash) ||
                references.Fact(hash) is not { } canonical) continue;
            Add(hash, $"fact-catalog/absent/{hash}", canonical);
        }
        return new FactCatalogInspection(rows, currentValue, bindRead);
    }

    private sealed record FactRow(string Key, string Hash, string Name, string Group, ReferenceRecord? Reference,
        int? Table, int Index, uint Value);
}
