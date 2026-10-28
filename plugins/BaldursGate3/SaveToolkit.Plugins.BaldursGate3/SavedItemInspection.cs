using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>LSF item occurrences across every owner; deliberately not a resolved player inventory.</summary>
internal sealed class SavedItemInspection
{
    internal const string Prefix = "saved-items/";
    private const int MaxItems = 100_000;
    private readonly Lazy<IReadOnlyList<Item>> _items;
    private readonly Bg3References _references;
    private readonly Func<string, string, FieldValue, FieldNode> _bind;
    private readonly Func<int, int, FieldNode?>? _bindQuantity;
    private readonly Func<int, int, Int128?>? _readQuantity;

    internal SavedItemInspection(IReadOnlyList<DocumentEntry> entries, Bg3References references,
        Func<string, string, FieldValue, FieldNode> bind, Func<int, int, FieldNode?>? bindQuantity = null,
        Func<int, int, Int128?>? readQuantity = null)
    {
        _items = new(() => Read(entries));
        _references = references;
        _bind = bind;
        _bindQuantity = bindQuantity;
        _readQuantity = readQuantity;
    }

    internal IReadOnlyList<ViewNode> Build()
    {
        var collection = Collection;
        return
        [
            new TextNode("Saved Item nodes from Globals.lsf, including world/trader items. Explicit numeric quantity candidates can be edited experimentally; live ownership, equipment and NewAge stacks remain unresolved. No party gold total is calculated. Not validated on console; edited saves may be rejected.") { Tone = Tone.Warning },
            new QueryableTableNode(collection.Id, collection.Title, collection.Columns, collection.TotalCount) { Description = collection.Description },
            SectionNode.Lazy("Browse saved items with provenance (all owners)", Prefix + "all"),
        ];
    }

    internal QueryableCollection Collection => new(
        "bg3.saved-items", "Saved item occurrences",
        [
            new QueryableColumn("name", "Candidate name") { Width = ColumnWidth.Wide },
            new QueryableColumn("template", "CurrentTemplate") { Width = ColumnWidth.Wide },
            new QueryableColumn("stats", "Stats ID"),
            new QueryableColumn("ownership", "Original owner / quantity") { Width = ColumnWidth.Wide },
            new QueryableColumn("quantity", "Explicit quantity candidate"),
        ], _items.Value.Count)
    {
        Description = "All original Globals.lsf Item nodes, including world and trader items. Ownership and quantity are shown only when an explicit LSF attribute is present; no positional join is inferred.",
    };

    internal QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        var items = _items.Value;
        var text = query.Text?.Trim();
        var filtered = items.Where(item => string.IsNullOrEmpty(text) ||
            item.Template.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            item.Stats.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            (_references.Template(item.Template)?.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));
        var ordered = query.SortColumn switch
        {
            null => filtered.OrderBy(item => item.Key, StringComparer.Ordinal),
            "name" => query.Descending ? filtered.OrderByDescending(item => _references.Template(item.Template)?.Name ?? item.Template, StringComparer.OrdinalIgnoreCase) : filtered.OrderBy(item => _references.Template(item.Template)?.Name ?? item.Template, StringComparer.OrdinalIgnoreCase),
            "template" => query.Descending ? filtered.OrderByDescending(item => item.Template, StringComparer.Ordinal) : filtered.OrderBy(item => item.Template, StringComparer.Ordinal),
            "stats" => query.Descending ? filtered.OrderByDescending(item => item.Stats, StringComparer.Ordinal) : filtered.OrderBy(item => item.Stats, StringComparer.Ordinal),
            "ownership" => query.Descending ? filtered.OrderByDescending(item => Ownership(item), StringComparer.Ordinal) : filtered.OrderBy(item => Ownership(item), StringComparer.Ordinal),
            "quantity" => query.Descending ? filtered.OrderByDescending(item => _readQuantity?.Invoke(item.Entry, item.Node)) : filtered.OrderBy(item => _readQuantity?.Invoke(item.Entry, item.Node)),
            _ => throw new ArgumentException($"Unknown BG3 saved-item sort column '{query.SortColumn}'.", nameof(query)),
        };
        var total = ordered.LongCount();
        var pageItems = ordered.ThenBy(item => item.Key, StringComparer.Ordinal).Skip(query.Offset).Take(query.Limit).ToArray();
        var rows = pageItems.Select(Row).ToArray();
        return new QueryableCollectionPage("bg3.saved-items", Collection.Columns, rows,
            items.Count, total, query.Offset, query.Limit);
    }

    internal IReadOnlyList<ViewNode> Expand(string id)
    {
        try { return ExpandSupported(id); }
        catch (SaveFormatException exception)
        {
            return [new TextNode($"Saved-item inspection unavailable: {exception.Message} Original resource bytes remain available raw.") { Tone = Tone.Warning }];
        }
    }

    private IReadOnlyList<ViewNode> ExpandSupported(string id)
    {
        var items = _items.Value;
        if (id == Prefix + "all")
            return [new StatsNode([new Stat("Saved item occurrences", items.Count.ToString(CultureInfo.InvariantCulture))]),
                .. ResourceExplorer.Pages("Saved items", Prefix.TrimEnd('/'), items.Count)];
        if (id.Split('/') is not ["saved-items", var startText, var countText] ||
            !int.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            start > items.Count || count > items.Count - start)
            throw new KeyNotFoundException("Invalid BG3 saved-item page.");
        if (count > ResourceExplorer.PageSize)
            return ResourceExplorer.Pages("Saved items", Prefix.TrimEnd('/'), count, start);
        return [new TableNode([new("name", "Candidate name"), new("template", "CurrentTemplate"), new("stats", "Stats ID"),
            new("provenance", "Occurrence provenance"), new("ownership", "Original owner / quantity"), new("quantity", "Explicit quantity candidate")],
            ResourceExplorer.Range(start, count, items.Count).Select(index => Row(items[index])).ToArray())
        {
            Title = "Original saved items — all owners",
            Description = "Names retain source attribution in each cell. Missing reference records preserve raw IDs; repeated templates are not merged.",
        }];
    }

    private TableRow Row(Item item)
    {
        var record = _references.Template(item.Template);
        var name = _bind($"{item.Key}/name", "Candidate item name", FieldValue.Of(record?.Name ?? item.Template));
        return new TableRow(item.Key, new Dictionary<string, ViewNode>
        {
            ["name"] = Bg3References.Describe(name, record, item.Template),
            ["template"] = Bg3References.Describe(_bind($"{item.Key}/template", "CurrentTemplate", FieldValue.Of(item.Template)), record, item.Template)
                with { Spec = new ReferenceSpec(Bg3References.Templates) { ReadOnly = true, AllowCustom = true } },
            ["stats"] = _bind($"{item.Key}/stats", "Stats ID", FieldValue.Of(item.Stats)),
            ["provenance"] = new TextNode($"Globals entry {item.Entry}, node {item.Node}; template occurrence {item.Occurrence}"),
            ["ownership"] = new TextNode(Ownership(item)),
            ["quantity"] = (ViewNode?)_bindQuantity?.Invoke(item.Entry, item.Node) ?? new TextNode("Unavailable: no unambiguous positive integer quantity attribute. Inspect raw resources for other primitives."),
        });
    }

    private static string Ownership(Item item) => item.Owner is null && item.Quantity is null
        ? "Unknown / unknown"
        : $"Candidate owner: {item.Owner ?? "unknown"}; candidate quantity: {item.Quantity ?? "unknown"}. Explicit attributes only; join is unverified.";

    private static IReadOnlyList<Item> Read(IReadOnlyList<DocumentEntry> entries)
    {
        var items = new List<Item>();
        for (var entry = 0; entry < entries.Count; entry++)
        {
            if (!entries[entry].Name.Equals("Globals.lsf", StringComparison.OrdinalIgnoreCase) ||
                entries[entry].Resource is not { } resource) continue;
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var node = 0; node < resource.Nodes.Count; node++)
            {
                var current = resource.Nodes[node];
                if (current.Name != "Item" || current.Parent < 0 || resource.Nodes[current.Parent].Name != "Items") continue;
                BinaryData.Require(items.Count < MaxItems, "BG3 saved-item inspection budget exceeded.");
                var template = Value(resource, current, "CurrentTemplate", uuid: true) ?? "(missing template)";
                var occurrence = occurrences.GetValueOrDefault(template) + 1;
                occurrences[template] = occurrence;
                items.Add(new Item(entry, node, template, Value(resource, current, "Stats", uuid: false) ?? "(missing Stats)", occurrence,
                    Candidate(resource, current, ["Owner", "OwnerId", "OwnerEntity", "InventoryOwner"]),
                    Candidate(resource, current, ["Quantity", "Amount", "Count", "StackAmount", "StackCount"])));
            }
        }

        return items;
    }

    private static string? Value(LarianResource resource, ResourceNode node, string name, bool uuid)
    {
        var matches = node.Attributes.Select(index => resource.Attributes[index]).Where(a => a.Name == name).Take(2).ToArray();
        if (matches is not [var attribute] || attribute.Data.Length > ResourceExplorer.MaxInlineBytes ||
            (uuid ? attribute.Type != LarianAttributeType.UUID : !AttributeDecoder.IsString(attribute.Type))) return null;
        return AttributeDecoder.Read(attribute).AsString();
    }

    private static string? Candidate(LarianResource resource, ResourceNode node, IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            var value = node.Attributes.Select(index => resource.Attributes[index]).Where(a => a.Name == name).Take(2).ToArray();
            if (value is [var attribute] && attribute.Data.Length <= ResourceExplorer.MaxInlineBytes &&
                (AttributeDecoder.IsString(attribute.Type) || attribute.Type is LarianAttributeType.Byte or LarianAttributeType.Short or
                    LarianAttributeType.UShort or LarianAttributeType.Int or LarianAttributeType.UInt or LarianAttributeType.Int8 or
                    LarianAttributeType.Int64 or LarianAttributeType.Long or LarianAttributeType.ULongLong))
            {
                return AttributeDecoder.Read(attribute).ToString();
            }
        }

        return null;
    }

    private sealed record Item(int Entry, int Node, string Template, string Stats, int Occurrence,
        string? Owner, string? Quantity)
    {
        internal string Key => $"saved-item/{Entry}/{Uri.EscapeDataString(Template)}/occurrence/{Occurrence}";
    }
}
