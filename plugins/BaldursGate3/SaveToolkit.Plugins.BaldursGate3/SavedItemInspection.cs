using System.Globalization;
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

    internal SavedItemInspection(IReadOnlyList<DocumentEntry> entries, Bg3References references,
        Func<string, string, FieldValue, FieldNode> bind)
    {
        _items = new(() => Read(entries));
        _references = references;
        _bind = bind;
    }

    internal IReadOnlyList<ViewNode> Build() =>
    [
        new TextNode("Original saved Item nodes from Globals.lsf, including world/trader items. Ownership, equipment and quantity are unresolved; no party gold total is calculated. Reference names are candidates from an exact template GUID lookup.") { Tone = Tone.Info },
        SectionNode.Lazy("Browse saved items (all owners)", Prefix + "all"),
    ];

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
            new("provenance", "Occurrence provenance"), new("ownership", "Owner / quantity")],
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
            ["template"] = _bind($"{item.Key}/template", "CurrentTemplate", FieldValue.Of(item.Template)),
            ["stats"] = _bind($"{item.Key}/stats", "Stats ID", FieldValue.Of(item.Stats)),
            ["provenance"] = new TextNode($"Globals entry {item.Entry}, node {item.Node}; template occurrence {item.Occurrence}"),
            ["ownership"] = new TextNode("Unknown / unknown"),
        });
    }

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
                items.Add(new Item(entry, node, template, Value(resource, current, "Stats", uuid: false) ?? "(missing Stats)", occurrence));
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

    private sealed record Item(int Entry, int Node, string Template, string Stats, int Occurrence)
    {
        internal string Key => $"saved-item/{Entry}/{Uri.EscapeDataString(Template)}/occurrence/{Occurrence}";
    }
}
