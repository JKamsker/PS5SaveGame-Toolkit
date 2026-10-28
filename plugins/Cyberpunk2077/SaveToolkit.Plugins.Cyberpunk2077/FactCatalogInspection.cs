using System.Globalization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal static class FactCatalogInspection
{
    public const string CollectionId = "fact-catalog";
    public static InspectionTable Build(IReadOnlyDictionary<int, IReadOnlyList<SaveFact>> tables, CyberpunkReferences references,
        Func<int, int, uint>? currentValue = null)
    {
        var rows = new List<InspectionRow>();
        var saved = new HashSet<uint>();
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
                Add(fact.Hash, $"fact-catalog/saved/{table.Key}/{i}", table.Key.ToString(CultureInfo.InvariantCulture), fact.Value.ToString(CultureInfo.InvariantCulture));
            }
        }
        foreach (var record in CyberpunkReferences.All(references.Facts))
        {
            if (!uint.TryParse(record.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var hash) || references.Fact(hash) is null || saved.Contains(hash)) continue;
            Add(hash, $"fact-catalog/absent/{hash}", "", "");
        }
        return new InspectionTable(CollectionId, "Quest facts: merged catalog and save (read-only)", [new("hash", "FNV-1a/32"),
            new("name", "Exact canonical identifier"), new("group", "Identifier prefix (not a quest title)"),
            new("presence", "Presence"), new("value", "Raw uint32", IntegerSpec.UInt32), new("table", "Table node", IntegerSpec.UInt32),
            new("evidence", "Interpretation")], rows,
            "F8 search/filter/sort covers the complete merged facts catalog plus every saved entry, including unknown hashes and repeated entries. " +
            "Use prefix, presence and raw-value filters to inspect progress evidence. Catalog-only rows have no stored value; no completion recipe. " +
            "Existing raw values can be edited in Saved facts with warnings.", (row, column) =>
            {
                if (column != "value" || currentValue is null || row.Values["table"].Length == 0) return row.Values.GetValueOrDefault(column, "");
                var parts = row.Key.Split('/');
                return currentValue(int.Parse(parts[2], CultureInfo.InvariantCulture), int.Parse(parts[3], CultureInfo.InvariantCulture))
                    .ToString(CultureInfo.InvariantCulture);
            });
    }
}
