using System.Globalization;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed record ProgressEntry(ReferenceRecord Reference, uint EventId, int Offset, int Bit,
    string Region, string RegionId)
{
    public string Key => Reference.Key;
    public string Category => Reference.Category!;
    public string ValueId(int slot) => $"slots/{slot}/progress/{Key}/raw";
    public string RowId(int slot) => $"slots/{slot}/progress/{Key}";
}

internal sealed record ProgressRegion(string Id, string Title, IReadOnlyList<ProgressEntry> Entries);
internal sealed record ProgressIndex(IReadOnlyList<ProgressRegion> Regions, string Notice);

internal static class ProgressReferences
{
    public const string CatalogId = "eldenring-progress";
    public const string ClassCatalogId = "eldenring-classes";
    private const int MaximumRecords = 4096;
    private const int RegionPageSize = 100;
    private const int MaximumKeyLength = 160;
    private const int MaximumLabelLength = 200;
    internal const int BitsPerByte = 8;
    public const string Warning = "Read-only Inferred source candidates, not console-validated PS5 progress. "
        + "A set/unset source bit does not prove discovery, boss defeat, historical collection or quest completion. "
        + "Statuses stay Unknown. No summoning-pool, quest, DLC or NG+ mapping is claimed.";

    public static ProgressIndex Read(IReferenceCatalog? catalog)
    {
        if (catalog is null) return new([], "No compatible progress reference pack; raw inspection remains available.");
        if (catalog.Count is < 0 or > MaximumRecords)
            return new([], $"Progress catalog exceeds the bounded {MaximumRecords}-record budget; no candidate mappings loaded.");
        var entries = new List<ProgressEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rejected = 0;
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            var page = catalog.Search(new() { Offset = offset, Limit = ReferenceQuery.MaxLimit });
            if (page.Items.Count is 0 or > ReferenceQuery.MaxLimit)
                return new([], "Progress catalog returned an invalid page; no candidate mappings loaded.");
            foreach (var record in page.Items)
            {
                if (!seen.Add(record.Key) || Parse(record) is not { } entry) rejected++;
                else entries.Add(entry);
            }
        }

        var regions = entries.GroupBy(entry => (entry.Category, entry.RegionId))
            .OrderBy(group => group.Key.Category, StringComparer.Ordinal)
            .ThenBy(group => group.Key.RegionId, StringComparer.Ordinal)
            .SelectMany(group => group.OrderBy(entry => entry.Key, StringComparer.Ordinal).Chunk(RegionPageSize)
                .Select((page, index) => new ProgressRegion($"{group.Key.Category}/{group.Key.RegionId}/{index}",
                    $"{group.Key.Category} · {page[0].Region}" + (group.Count() > RegionPageSize ? $" · page {index + 1}" : ""), page)))
            .ToArray();
        return new(regions, $"Loaded {entries.Count} bounded candidate mappings; rejected {rejected} malformed/duplicate records. "
            + Warning + " " + catalog.Dataset.Attribution.ToShortText());
    }

    internal static ProgressEntry? Parse(ReferenceRecord record)
    {
        var attributes = record.Attributes;
        if (record.Category is not ("grace" or "boss" or "map-control")
            || !Identifier(record.Key) || !record.Key.StartsWith(record.Category + ":", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(record.Name) || record.Name.Length > MaximumLabelLength
            || !attributes.TryGetValue("schema", out var schema) || schema != "eldenring-progress-candidate/1"
            || !attributes.TryGetValue("region", out var region) || string.IsNullOrWhiteSpace(region) || region.Length > MaximumLabelLength
            || !attributes.TryGetValue("regionId", out var regionId) || !Identifier(regionId)
            || !attributes.TryGetValue("eventId", out var eventText)
            || !uint.TryParse(eventText, NumberStyles.None, CultureInfo.InvariantCulture, out var eventId)
            || !Number(attributes, "offset", out var offset) || offset >= SemanticLayout.EventSize
            || !Number(attributes, "bit", out var bit) || bit >= BitsPerByte)
            return null;
        return new(record, eventId, offset, bit, region, regionId);
    }

    private static bool Number(IReadOnlyDictionary<string, string> attributes, string key, out int value)
    {
        value = 0;
        return attributes.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool Identifier(string value) => value.Length is > 0 and <= MaximumKeyLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '_' or '-');
}
