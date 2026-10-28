using System.Globalization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal static class FactCatalogInspection
{
    public const string CollectionId = "fact-catalog";
    public static InspectionTable Build(IReadOnlyDictionary<int, IReadOnlyList<SaveFact>> tables, CyberpunkReferences references,
        Func<int, int, uint>? currentValue = null,
        Func<string, string, FieldSpec, Func<FieldValue>, FieldNode>? bindRead = null)
    {
        var rows = new List<InspectionRow>();
        var saved = new HashSet<uint>();
        var values = new Dictionary<string, Func<FieldValue>>(StringComparer.Ordinal);
        void Add(uint hash, string key, string table, string value)
        {
            var record = references.Fact(hash);
            var canonical = record?.Attributes.GetValueOrDefault("canonicalName") ?? record?.Name;
            var name = canonical ?? $"0x{hash:X8}";
            var separator = name.IndexOf('_');
            var group = canonical is null ? "Unknown" : separator > 0 ? name[..separator] : "Other identifiers";
            rows.Add(new InspectionRow(key, new Dictionary<string, string>
            {
                ["hash"] = $"0x{hash:X8}", ["name"] = name, ["group"] = group,
                ["presence"] = value.Length == 0 ? "Catalog only / absent from save" : "Present in save",
                ["value"] = value, ["table"] = table,
                ["evidence"] = "Exact canonical FNV-1a/32 join only. " +
                    (canonical is not null && canonical != canonical.Trim() ? "Significant surrounding whitespace retained. " : "") +
                    "Absence is not zero; raw values, debug flags and prefixes do not establish active quest stage or completion.",
            }, record?.AttributionText ?? "Unknown saved hash; no unambiguous canonical match.",
                record is null ? Confidence.Unknown : Confidence.Inferred));
        }
        foreach (var table in tables)
        {
            for (var i = 0; i < table.Value.Count; i++)
            {
                var fact = table.Value[i];
                saved.Add(fact.Hash);
                var index = i;
                var key = $"fact-catalog/saved/{table.Key}/{i}";
                values.Add(key, () => FieldValue.Of((ulong)(currentValue?.Invoke(table.Key, index) ?? fact.Value)));
                Add(fact.Hash, key, table.Key.ToString(CultureInfo.InvariantCulture), fact.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
        foreach (var record in CyberpunkReferences.All(references.Facts))
        {
            if (!uint.TryParse(record.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var hash) || references.Fact(hash) is null || saved.Contains(hash)) continue;
            Add(hash, $"fact-catalog/absent/{hash}", "", "");
        }
        var nullableValue = new NullableSpec(IntegerSpec.UInt32) { ReadOnly = true };
        string Current(InspectionRow row) => values.TryGetValue(row.Key, out var get) ? get().ToString() : "";
        return new InspectionTable(CollectionId, "Quest facts: merged catalog and save (read-only)", [new("hash", "FNV-1a/32"),
            new("name", "Exact canonical identifier"), new("group", "Identifier prefix (not a quest title)"),
            new("presence", "Presence"), new("value", "Raw uint32 / absent", nullableValue), new("table", "Table node", IntegerSpec.UInt32),
            new("evidence", "Interpretation")], rows,
            "F8 search/filter/sort covers the complete merged facts catalog plus every saved entry, including unknown hashes and repeated entries. " +
            "Use prefix, presence and raw-value filters to inspect progress evidence. Catalog-only rows have no stored value; no completion recipe. " +
            "Existing raw values can be edited in Saved facts with warnings.", (row, column) =>
            {
                return column == "value" ? Current(row) : row.Values.GetValueOrDefault(column, "");
            }, (row, column) =>
            {
                if (column != "value" || bindRead is null) return null;
                var get = values.GetValueOrDefault(row.Key) ?? (() => FieldValue.Null);
                var field = bindRead(row.Key + "/value", "Saved fact value (null = absent)", nullableValue, get) with
                { Confidence = Confidence.Unknown, Description = row.Attribution + "\nAbsent catalog facts have no saved value; zero is a stored value. Edit existing values in Saved facts." };
                return new SectionNode(Current(row), [field, new TextNode(row.Attribution)])
                    { Description = field.Description, Badge = row.Confidence.ToString() };
            });
    }
}
