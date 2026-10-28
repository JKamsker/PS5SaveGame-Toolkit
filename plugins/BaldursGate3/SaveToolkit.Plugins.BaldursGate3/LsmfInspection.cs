using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed class LsmfInspection
{
    internal const string Prefix = "lsmf/";
    private const int MaxSources = 16;
    private readonly Dictionary<string, Source> _sources = new(StringComparer.Ordinal);
    private readonly Func<string, string, FieldValue, FieldNode> _bind;

    internal LsmfInspection(IReadOnlyList<DocumentEntry> entries, Func<string, string, FieldValue, FieldNode> bind)
    {
        _bind = bind;
        for (var entry = 0; entry < entries.Count; entry++)
        {
            if (entries[entry].Resource is not { } resource) continue;
            foreach (var node in resource.Nodes.Where(n => n.Name == "NewAge"))
            foreach (var attribute in node.Attributes)
            {
                var value = resource.Attributes[attribute];
                if (value is not { Name: "NewAge", Type: LarianAttributeType.ScratchBuffer }) continue;
                // Bound optional inspection independently of the original resource explorer.
                if (_sources.Count == MaxSources) return;
                var key = $"{Prefix}{entry}/{attribute}";
                _sources.TryAdd(key, new Source(entries[entry].Name, entry, attribute,
                    new Lazy<LsmfModel>(() => LsmfReader.Read(value.Data))));
            }
        }
    }

    internal IReadOnlyList<ViewNode> Build() =>
    [
        new TextNode("Read-only original NewAge structure. Nominal component rows do not establish gameplay values, ownership or quantities. At most 16 typed NewAge sources are inspected.") { Tone = Tone.Info },
        .. _sources.Select(pair => (ViewNode)SectionNode.Lazy(pair.Value.Name, pair.Key)),
        .. _sources.Count == 0 ? new ViewNode[] { new TextNode("No typed NewAge ScratchBuffer is present.") } : [],
    ];

    internal IReadOnlyList<ViewNode> Expand(string id)
    {
        var parts = id.Split('/');
        if (parts.Length < 3 || !_sources.TryGetValue(string.Join('/', parts.Take(3)), out var source))
            throw new KeyNotFoundException("Unknown BG3 NewAge source.");
        try
        {
            var model = source.Model.Value;
            var prefix = string.Join('/', parts.Take(3));
            if (parts.Length == 3)
            {
                return
                [
                    _bind($"{prefix}/sha256", "Whole NewAge SHA-256", FieldValue.Of(Hash(model.Bytes.Span))) with
                    {
                        Confidence = Confidence.Unknown,
                        Description = "Fingerprint of every original NewAge byte, including unknown regions; no integrity algorithm is implied.",
                    },
                    new TextNode("Owner lists, packed Stats/Health, enum pools and stack groups are not interpreted. Unknown components remain untouched. Format facts: https://github.com/DanFinchUK/bg3-savefile-parser/tree/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8 (research only; no implementation copied)."),
                    SectionNode.Lazy("Live state candidates (read-only)", $"{prefix}/state"),
                    .. ResourceExplorer.Pages("Components", $"{prefix}/components", model.Components.Count),
                    SectionNode.Lazy("EntityId occurrences (not party identities)", $"{prefix}/entities"),
                    new HexNode($"attribute/{source.Entry}/{source.Attribute}", model.Bytes.Length) { Title = "Original NewAge bytes" },
                ];
            }

            if (parts is [_, _, _, "entities"])
                return ResourceExplorer.Pages("Entity occurrences", $"{prefix}/entities", source.Entities.Value.Count);
            if (parts is [_, _, _, "state"])
                return [State(model, prefix)];
            if (parts.Length != 6 || !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                throw new KeyNotFoundException("Invalid BG3 NewAge page.");
            var total = parts[3] switch
            {
                "components" => model.Components.Count,
                "entities" => source.Entities.Value.Count,
                _ => throw new KeyNotFoundException("Unknown BG3 NewAge collection."),
            };
            if (start > total || count > total - start) throw new KeyNotFoundException("Invalid BG3 NewAge range.");
            if (count > ResourceExplorer.PageSize)
                return ResourceExplorer.Pages(parts[3], $"{prefix}/{parts[3]}", count, start);
            return [parts[3] == "components" ? Components(model, prefix, start, count) : Entities(source, prefix, start, count)];
        }
        catch (SaveFormatException exception)
        {
            return [new TextNode($"NewAge inspection unavailable: {exception.Message} Original data is preserved in Resources / Raw.") { Tone = Tone.Warning }];
        }
    }

    private TableNode Components(LsmfModel model, string prefix, int start, int count) => new(
        [new("name", "Component"), new("rows", "Nominal rows"), new("width", "Element bytes"), new("hash", "Nominal bytes SHA-256")],
        ResourceExplorer.Range(start, count, model.Components.Count).Select(index =>
        {
            var component = model.Components[index];
            var key = $"{prefix}/component/{Uri.EscapeDataString(component.Name)}";
            return new TableRow(key, new Dictionary<string, ViewNode>
            {
                ["name"] = new TextNode(component.Name),
                ["rows"] = _bind($"{key}/rows", "Nominal rows", FieldValue.Of(component.Rows)),
                ["width"] = _bind($"{key}/width", "Element bytes", FieldValue.Of(component.ElementBytes)),
                ["hash"] = _bind($"{key}/sha256", "Nominal bytes SHA-256", FieldValue.Of(Hash(model.NominalData(component).Span))) with
                {
                    Confidence = Confidence.Unknown,
                    Description = "Descriptor range fingerprint only. Packed streams may extend beyond this nominal range; see the whole NewAge fingerprint.",
                },
            });
        }).ToArray()) { Title = "Original LSMF component directory" };

    private TableNode Entities(Source source, string prefix, int start, int count) => new(
        [new("guid", "EntityId"), new("row", "Source row"), new("copies", "GUID occurrences")],
        ResourceExplorer.Range(start, count, source.Entities.Value.Count).Select(index =>
        {
            var entity = source.Entities.Value[index];
            var key = $"{prefix}/entity/{entity.Key}";
            return new TableRow(key, new Dictionary<string, ViewNode>
            {
                ["guid"] = _bind($"{key}/guid", "EntityId", FieldValue.Of(entity.Guid)),
                ["row"] = new TextNode(entity.Row.ToString(CultureInfo.InvariantCulture)),
                ["copies"] = new TextNode($"{entity.Occurrence}/{entity.Copies}; identity join unresolved"),
            });
        }).ToArray()) { Title = "EntityId occurrence provenance", Description = "Duplicate GUIDs remain separate. No party, component-owner or body/stats join is inferred." };

    private static TableNode State(LsmfModel model, string prefix) => new(
        [new("component", "Component"), new("rows", "Rows"), new("bytes", "Element bytes"), new("interpretation", "Read-only interpretation")],
        model.Components.Where(c => IsStateComponent(c.Name)).Select(component => new TableRow(
            $"{prefix}/state/{Uri.EscapeDataString(component.Name)}",
            new Dictionary<string, ViewNode>
            {
                ["component"] = new TextNode(component.Name),
                ["rows"] = new TextNode(component.Rows.ToString(CultureInfo.InvariantCulture)),
                ["bytes"] = new TextNode(component.ElementBytes.ToString(CultureInfo.InvariantCulture)),
                ["interpretation"] = new TextNode(Interpretation(component.Name)),
            })).ToArray())
    {
        Title = "Live-state component candidates",
        Description = "Component families are present in the bounded NewAge directory. Values, owner joins and authoritative-vs-cache semantics remain unproven without controlled native save pairs.",
    };

    private static bool IsStateComponent(string name) =>
        name.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Health", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Classes", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Stats", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("SpellBook", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Container", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("IsOwned", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("NewStack", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("StackEntry", StringComparison.OrdinalIgnoreCase);

    private static string Interpretation(string name) => name switch
    {
        var value when value.Contains("Character", StringComparison.OrdinalIgnoreCase) => "Character identity candidate; no party/avatar join inferred.",
        var value when value.Contains("Health", StringComparison.OrdinalIgnoreCase) => "Health-family candidate; base/current/temp semantics are unknown.",
        var value when value.Contains("Classes", StringComparison.OrdinalIgnoreCase) => "Class-family candidate; progression and respec semantics are unknown.",
        var value when value.Contains("SpellBook", StringComparison.OrdinalIgnoreCase) => "Spell selection candidate; source/prepared/virtual slots are unknown.",
        var value when value.Contains("Container", StringComparison.OrdinalIgnoreCase) => "Container membership candidate; recursive ownership is not decoded.",
        var value when value.Contains("Stack", StringComparison.OrdinalIgnoreCase) => "Stack-group candidate; member sums and settled state are not decoded.",
        var value when value.Contains("IsOwned", StringComparison.OrdinalIgnoreCase) => "Ownership flag candidate; owner identity is not decoded.",
        _ => "Raw component candidate; no gameplay meaning assigned.",
    };

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class Source(string name, int entry, int attribute, Lazy<LsmfModel> model)
    {
        internal string Name { get; } = name;
        internal int Entry { get; } = entry;
        internal int Attribute { get; } = attribute;
        internal Lazy<LsmfModel> Model { get; } = model;
        internal Lazy<IReadOnlyList<LsmfEntity>> Entities { get; } = new(() => model.Value.Entities());
    }
}
