using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Bounded, read-only saved identities; no quest completion or missing-recipe inference.</summary>
internal sealed class SavedWorldViews(SavedWorldData data, IReferenceCatalog? items)
{
    private const string Prefix = "saved/";
    private const int DetailPageSize = 64;
    // These projections cannot be edited. Build metadata only for queried kinds, once per document.
    private readonly Dictionary<string, IReadOnlyList<SavedRow>> _rows = new(StringComparer.Ordinal);
    private static IReadOnlyList<QueryableColumn> Columns => [new("id", "Saved identity"), new("kind", "Kind"),
        new("state", "Encoded state"), new("details", "Saved details")];
    private static IReadOnlyList<string> Kinds => ["recipes", "facts", "journal"];

    public IReadOnlyList<QueryableCollection> Collections => Kinds.Select(kind =>
        new QueryableCollection(Prefix + kind, Title(kind), Columns, Count(kind))).ToArray();

    public bool HandlesCollection(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);
    public bool HandlesExpansion(string id) => id.StartsWith("fact/", StringComparison.Ordinal) || id.StartsWith("journal/", StringComparison.Ordinal);

    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Read-only saved recipes, facts and journal paths. Native names and statuses come from the save. " +
            "Facts are individual stored entries, not quest-completion flags. Opaque fact-name bytes use hash identities. " +
            "Journal resource paths are not localized quest titles; empty resources retain GUID-only links. " +
            "Absence from an array does not establish that a recipe is obtainable or missing."),
        .. data.Warnings.Select(w => (ViewNode)new TextNode(w) { Tone = Tone.Warning }),
        .. Kinds.Select(kind => Available(kind)
            ? (ViewNode)new QueryableTableNode(Prefix + kind, Title(kind), Columns, Count(kind))
            : new TextNode($"{Title(kind)} unavailable; use the preserving raw explorer.") { Tone = Tone.Warning }),
    ];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var kind = collectionId[Prefix.Length..];
        if (!Kinds.Contains(kind, StringComparer.Ordinal) || query.Cursor is not null || query.Offset < 0 ||
            query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(f => f.Key is not ("id" or "kind" or "state" or "details") || f.Value.Length > QueryableCollectionLimits.MaxFilterLength) ||
            query.SortColumn is not (null or "id" or "kind" or "state" or "details"))
        {
            throw new ArgumentException("Unsupported saved-data query.");
        }

        var rows = Rows(kind).Where(row => Matches(row, query));
        var ordered = (query.Descending ? rows.OrderByDescending(r => Cell(r, query.SortColumn), StringComparer.OrdinalIgnoreCase) :
            rows.OrderBy(r => Cell(r, query.SortColumn), StringComparer.OrdinalIgnoreCase)).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new QueryableCollectionPage(collectionId, Columns, ordered.Skip(query.Offset).Take(limit).Select(Row).ToArray(),
            Count(kind), ordered.Length, query.Offset, limit);
    }

    public IReadOnlyList<ViewNode> Expand(string id)
    {
        var parts = id.Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[2], out var start) || start < 0) { throw new KeyNotFoundException(id); }
        if (parts[0] == "fact" && int.TryParse(parts[1], out var index) && data.Facts is { } facts && index >= 0 && index < facts.Count && start <= facts[index].Entries.Count)
        {
            var fact = facts[index];
            return [new TextNode($"{fact.Name}: opaque word 0x{fact.OpaqueWord:X4}; name payload {fact.NameBytes.Length} bytes, opaque={fact.OpaqueName}. " +
                    "Time/expiry bits are preserved; no expiry or completion rule is assumed."),
                new TableNode([new("value", "Stored int16"), new("time", "Time bits"), new("expiry", "Expiry bits")],
                    fact.Entries.Skip(start).Take(DetailPageSize).Select((value, offset) => new TableRow((start + offset).ToString(CultureInfo.InvariantCulture),
                        new Dictionary<string, ViewNode> { ["value"] = new TextNode(value.StoredValue.ToString(CultureInfo.InvariantCulture)),
                            ["time"] = new TextNode($"0x{value.TimeBits:X8}"), ["expiry"] = new TextNode($"0x{value.ExpiryBits:X8}") })).ToArray()),
                .. Next($"fact/{index}", start, fact.Entries.Count)];
        }

        if (parts[0] == "journal" && data.Journal?.SingleOrDefault(e => e.Key == parts[1]) is { } entry && start <= entry.Path.Count)
        {
            return [new TableNode([new("resource", "Resource path"), new("guid", "Native GUID"), new("flags", "Raw flags")],
                entry.Path.Skip(start).Take(DetailPageSize).Select((link, offset) => new TableRow((start + offset).ToString(CultureInfo.InvariantCulture),
                    new Dictionary<string, ViewNode> { ["resource"] = new TextNode(link.Resource), ["guid"] = new TextNode(link.Guid.ToString("D")),
                        ["flags"] = new TextNode($"0x{link.Flags:X2}") })).ToArray()), .. Next($"journal/{entry.Key}", start, entry.Path.Count)];
        }

        throw new KeyNotFoundException(id);
    }

    private bool Available(string kind) => kind switch { "recipes" => data.Recipes is not null, "facts" => data.Facts is not null, _ => data.Journal is not null };
    private int Count(string kind) => kind switch { "recipes" => data.Recipes?.Count ?? 0, "facts" => data.Facts?.Count ?? 0, _ => data.Journal?.Count ?? 0 };
    private static string Title(string kind) => kind switch { "recipes" => "Saved recipes and schematics", "facts" => "Saved facts", _ => "Saved journal entries" };

    private IReadOnlyList<SavedRow> Rows(string kind)
    {
        if (!_rows.TryGetValue(kind, out var rows))
        {
            rows = CreateRows(kind).ToArray();
            _rows.Add(kind, rows);
        }
        return rows;
    }

    private IEnumerable<SavedRow> CreateRows(string kind)
    {
        if (kind == "recipes")
        {
            return (data.Recipes ?? []).Select(recipe => {
                var reference = items?.Find(recipe.NativeId);
                return new SavedRow($"{recipe.Kind}/{recipe.NativeId}", recipe.NativeId, recipe.Kind, "Recorded in player array",
                    () => reference is null ? new TextNode("Unmapped native recipe ID; no grant or initialization assumptions.") :
                        new SectionNode(reference.Name, [new TextNode(reference.Description ?? ""), new TextNode(reference.AttributionText)]) { Badge = "Inferred mapping" },
                    $"{recipe.NativeId}\n{recipe.Kind}\n{reference?.Name}\n{reference?.Description}");
            });
        }

        if (kind == "facts")
        {
            return (data.Facts ?? []).Select((fact, index) => new SavedRow(fact.Name, fact.Name, "Fact stream",
                $"{fact.Entries.Count} entries; opaque 0x{fact.OpaqueWord:X4}; opaque name={fact.OpaqueName}",
                () => SectionNode.Lazy("Raw value/time/expiry entries", $"fact/{index}/0"), fact.Name));
        }

        return (data.Journal ?? []).Select(entry => new SavedRow(entry.Key, entry.Path[^1].Resource + " | " + entry.Path[^1].Guid.ToString("D"),
            "Journal path", $"{entry.Status}; unread={entry.Unread}", () => SectionNode.Lazy("Full saved path", $"journal/{entry.Key}/0"),
            $"{entry.Status}\n{string.Join('\n', entry.Path.Select(link => $"{link.Resource}\n{link.Guid:D}"))}"));
    }

    private static string Cell(SavedRow row, string? column) => column switch { "kind" => row.Kind, "state" => row.State, "details" => row.Search, _ => row.Id };
    private static bool Matches(SavedRow row, QueryableCollectionQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Text) && !row.Contains(query.Text)) { return false; }
        if (query.Filters.Count == 0) { return true; }
        foreach (var filter in query.Filters)
        {
            if (!string.IsNullOrEmpty(filter.Value) && !string.Equals(Cell(row, filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase)) { return false; }
        }
        return true;
    }

    private static TableRow Row(SavedRow row) => new(row.Key, new Dictionary<string, ViewNode> {
        ["id"] = new TextNode(row.Id), ["kind"] = new TextNode(row.Kind), ["state"] = new TextNode(row.State), ["details"] = row.Details(),
    });
    private static IEnumerable<ViewNode> Next(string prefix, int start, int count) => start + DetailPageSize < count
        ? [SectionNode.Lazy("Next saved entries", $"{prefix}/{start + DetailPageSize}")] : [];
    private sealed record SavedRow(string Key, string Id, string Kind, string State, Func<ViewNode> Details, string Search)
    {
        public bool Contains(string text)
        {
            // Only multiline searches can cross the separators between fields. Preserve that behavior
            // without allocating or retaining a second copy of every record's searchable metadata.
            if (text.Contains('\n')) { return $"{Id}\n{Kind}\n{State}\n{Search}".Contains(text, StringComparison.OrdinalIgnoreCase); }
            return Id.Contains(text, StringComparison.OrdinalIgnoreCase) || Kind.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                State.Contains(text, StringComparison.OrdinalIgnoreCase) || Search.Contains(text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
