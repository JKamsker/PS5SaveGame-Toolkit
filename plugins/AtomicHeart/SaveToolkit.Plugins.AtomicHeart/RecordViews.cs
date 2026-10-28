using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed class RecordViews
{
    private sealed record PropertyGroup(IReadOnlyList<TaggedProperty> Members);
    private readonly RecordIndex index;
    private readonly Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind;
    private readonly Func<TaggedProperty, FieldValue> current;
    private readonly IReferenceCatalog? catalog;
    private readonly List<PropertyGroup> groups = [];
    private readonly Dictionary<TaggedProperty, int> childGroups = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> objectGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<TaggedProperty, string> propertyPaths = new(ReferenceEqualityComparer.Instance);
    private int pathUnits;

    internal RecordViews(RecordIndex index, Func<string, string, FieldSpec, Func<FieldValue>, FieldNode> bind,
        Func<TaggedProperty, FieldValue> current, IReferenceCatalog? catalog = null)
    {
        this.index = index;
        this.bind = bind;
        this.current = current;
        this.catalog = catalog;
        AddGroup("SlotData", index.Properties);
        foreach (var record in index.Objects) objectGroups.Add(record.Id, AddGroup(record.Id, record.Properties));
    }

    internal IReadOnlyList<ViewNode> BuildView() => [
        new TextNode("Offsets refer to decoded SlotData. Class strings and serialized names are raw. Native values and unknown tails remain available as hex; world records do not imply player ownership."),
        SectionNode.Lazy("SlotData tagged properties", "inspect/group/0/0"),
        new SectionNode("Levels", Enumerable.Range(0, (index.Levels.Count + AtomicHeartDocument.PageSize - 1) / AtomicHeartDocument.PageSize)
            .Select(page => (ViewNode)SectionNode.Lazy($"Level records {page * AtomicHeartDocument.PageSize + 1}–{Math.Min((page + 1) * AtomicHeartDocument.PageSize, index.Levels.Count)}",
                $"inspect/levels/{page * AtomicHeartDocument.PageSize}")).ToArray()),
        SectionNode.Lazy("Objects", "inspect/records/0"),
    ];

    internal IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        var parts = lazyId.Split('/');
        if (parts.Length < 3 || !int.TryParse(parts[2], out var ordinal) || ordinal < 0) throw new KeyNotFoundException(lazyId);
        if (parts.Length == 4 && parts[1] == "group" && ordinal < groups.Count &&
            int.TryParse(parts[3], out var start) && start >= 0 && (start == 0 || start < groups[ordinal].Members.Count))
            return Properties(ordinal, start);
        if (parts.Length != 3) throw new KeyNotFoundException(lazyId);
        if (parts[1] == "records" && (ordinal == 0 || ordinal < index.Objects.Count))
        {
            var nodes = index.Objects.Skip(ordinal).Take(AtomicHeartDocument.PageSize).Select((r, offset) =>
                (ViewNode)SectionNode.Lazy($"{r.Kind} · {r.Name} · {r.Owner}", $"inspect/object/{ordinal + offset}")).ToList();
            Continue(nodes, ordinal, index.Objects.Count, "records");
            return nodes;
        }
        if (parts[1] == "object" && ordinal < index.Objects.Count) return Object(index.Objects[ordinal]);
        if (parts[1] == "levels" && (ordinal == 0 || ordinal < index.Levels.Count))
        {
            var rows = index.Levels.Skip(ordinal).Take(AtomicHeartDocument.PageSize).Select(l => new TableRow(l.Id,
                new Dictionary<string, ViewNode> { ["name"] = new TextNode(l.Name), ["actors"] = new TextNode(l.ActorCount.ToString()),
                    ["span"] = new TextNode(Span(l.Span)) })).ToArray();
            var nodes = new List<ViewNode> { new TableNode([new("name", "Level"), new("actors", "Actors"), new("span", "Decoded span")], rows) };
            return nodes;
        }
        throw new KeyNotFoundException(lazyId);
    }

    private IReadOnlyList<ViewNode> Object(ObjectRecord record)
    {
        var nodes = new List<ViewNode> {
            new TextNode($"Identity: {record.Id}\nClass: {record.ClassPath}\nSecond class string: {record.ClassSubPath}\nRecord: {Span(record.Span)}; data: {Span(record.Payload)}") { Monospace = true },
            SectionNode.Lazy("Tagged properties", $"inspect/group/{objectGroups[record.Id]}/0"),
            Hex(record.Payload, "Original object payload"),
            Hex(record.Span, "Full record, including native suffix and components"),
        };
        if (record.UnknownTail.Length != 0) nodes.Add(Hex(record.UnknownTail, "Uninterpreted native payload / tail"));
        if (record.PrefixIds.Count != 0) nodes.Add(new TextNode($"Manual inventory prefix: {record.PrefixIds.Count} identifiers (not inferred quantities)."));
        if (record.Tags.Count != 0) nodes.Add(new TextNode($"Tags ({record.Tags.Count}): {string.Join(", ", record.Tags.Take(20).Select(t => t.Length > 256 ? t[..256] + "…" : t))}"));
        return nodes;
    }

    private IReadOnlyList<ViewNode> Properties(int groupId, int start)
    {
        var group = groups[groupId];
        var nodes = group.Members.Skip(start).Take(AtomicHeartDocument.PageSize).Select(property =>
        {
            var id = propertyPaths[property];
            if (property.Children.Count != 0)
                return (ViewNode)SectionNode.Lazy($"{property.Name} · {property.Detail} · {Span(property.Payload)}", $"inspect/group/{childGroups[property]}/0");
            if (property.Value is not { } value)
                return new SectionNode($"{property.Name} · {property.Type} {property.Detail}", [Hex(property.Payload, "Opaque property payload")]);
            var reference = property.Name is "PrimaryAssetName" or "ItemDataAsset" && value.Kind == FieldValueKind.Text
                ? catalog?.Find(InventoryState.ShortId(current(property).AsString())) : null;
            return bind(id, reference?.Name ?? property.Name, reference is null ? Spec(value) : new ReferenceSpec(InventoryViews.CatalogId),
                () => reference is null ? current(property) : FieldValue.Of(InventoryState.ShortId(current(property).AsString()))) with {
                Confidence = reference is null ? Confidence.Verified : Confidence.Inferred,
                Description = $"{property.Type} {property.Detail}; tag {Span(property.Tag)}; payload {Span(property.Payload)}. " +
                    (reference is null ? "" : $"Exact asset: {current(property).AsString()}. Inferred display label only; no blueprint ownership claim. {reference.AttributionText}"),
                Warning = "Serialized value only; gameplay meaning and edit limits are unverified.",
            };
        }).ToList();
        if (start + AtomicHeartDocument.PageSize < group.Members.Count)
            nodes.Add(SectionNode.Lazy("More properties", $"inspect/group/{groupId}/{start + AtomicHeartDocument.PageSize}"));
        return nodes;
    }

    private int AddGroup(string path, IReadOnlyList<TaggedProperty> members)
    {
        var id = groups.Count;
        groups.Add(new(members));
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in members)
        {
            var propertyPath = $"{path}/{Uri.EscapeDataString(property.Name)}[{property.ArrayIndex}]";
            var occurrence = occurrences.GetValueOrDefault(propertyPath);
            occurrences[propertyPath] = occurrence + 1;
            if (occurrence != 0) propertyPath += $"#{occurrence + 1}";
            pathUnits = checked(pathUnits + propertyPath.Length);
            if (pathUnits > RecordIndex.MaxIndexPathUnits) throw new SaveFormatException("Property identity text exceeds the index budget.");
            propertyPaths.Add(property, propertyPath);
            if (property.Children.Count != 0) childGroups.Add(property, AddGroup(propertyPath, property.Children));
        }
        return id;
    }

    private static FieldSpec Spec(FieldValue value) => value.Kind switch {
        FieldValueKind.Bool => new BoolSpec(), FieldValueKind.Integer => new IntegerSpec(Int128.MinValue, Int128.MaxValue),
        FieldValueKind.Float => new FloatSpec(), _ => new TextSpec(),
    };
    private static void Continue(List<ViewNode> nodes, int start, int total, string kind)
    {
        if (start + AtomicHeartDocument.PageSize < total)
            nodes.Add(SectionNode.Lazy("More", $"inspect/{kind}/{start + AtomicHeartDocument.PageSize}"));
    }
    internal static string Span(ByteSpan span) => $"0x{span.Offset:X} + {span.Length} bytes";
    internal static HexNode Hex(ByteSpan span, string title) => new($"plain/{span.Offset}/{span.Length}", span.Length) { Title = title };
}
