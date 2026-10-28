using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

internal sealed class UnrealGvasDocument : BoundSaveDocument
{
    private const int PageSize = 100;
    private const string EditWarning = "Serialized property name and type are verified. Game constraints and effects are unverified; keep a backup.";
    private readonly string _path;
    private readonly GvasEnvelope _envelope;
    private readonly Dictionary<string, GvasNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<GvasNode, string> _ids = [];
    private readonly Dictionary<string, Func<IReadOnlyList<ViewNode>>> _lazy = new(StringComparer.Ordinal);
    private readonly string? _readOnlyReason;

    public UnrealGvasDocument(string path, GvasEnvelope envelope)
    {
        _path = path;
        _envelope = envelope;
        Index(envelope.File.Properties, "property");
        if (envelope.IsWrapped) _readOnlyReason = "This compressed envelope is read-only; recompression has not been verified.";
        else if (_nodes.Values.Any(n => n.Name.Contains("crc", StringComparison.OrdinalIgnoreCase) ||
                 n.Name.Contains("checksum", StringComparison.OrdinalIgnoreCase) || n.Name.Contains("signature", StringComparison.OrdinalIgnoreCase)))
            _readOnlyReason = "This save contains a game-specific integrity field. Its coverage is unknown, so the entire document is read-only.";
    }

    public override string Title => _envelope.File.Header.SaveClass;
    public override bool CanEdit => _readOnlyReason is null;

    public override ViewNode BuildView()
    {
        var file = _envelope.File;
        var header = file.Header;
        var primitives = file.Properties.Where(n => n.Scalar is not null).ToArray();
        return new TabsNode([
            new Tab("Overview", [
                new StatsNode([
                    new Stat("Save version", header.SaveVersion.ToString()),
                    new Stat("Package version", header.PackageVersion + (header.PackageVersionUe5 is { } ue5 ? $" / UE5 {ue5}" : "")),
                    new Stat("Engine", $"{header.EngineMajor}.{header.EngineMinor}.{header.EnginePatch} / {header.EngineBuild}"),
                    new Stat("Custom versions", header.CustomVersionCount.ToString()),
                    new Stat("Top-level properties", file.Properties.Count.ToString()),
                ]),
                new TextNode($"Save class: {header.SaveClass}"),
                new TextNode($"Branch: {header.EngineBranch}. {_envelope.Label}."),
                new TextNode(_readOnlyReason ?? "Experimental. Embedded compressed payloads and unknown native structures are preserved read-only.") { Tone = Tone.Warning },
            ]),
            new Tab("Properties", [Lazy("Property tree", "tree", () => TreePage(file.Properties, 0, "tree"))]),
            new Tab("Search", [Lazy("Top-level primitive properties", "search", () => SearchPage(primitives, 0))]),
        ]);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _lazy.TryGetValue(lazyId, out var load)
        ? load() : throw new KeyNotFoundException($"Unknown GVAS section '{lazyId}'.");

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        if (!_nodes.TryGetValue(blobId, out var node)) throw new KeyNotFoundException(blobId);
        if (offset < 0 || count < 0 || offset > node.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        return _envelope.File.Original.Slice(node.Offset + checked((int)offset), Math.Min(count, node.Length - (int)offset));
    }

    public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(_path, _envelope.Serialize())];

    private void Index(IReadOnlyList<GvasNode> nodes, string prefix)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var id = $"{prefix}/{i}";
            _nodes.Add(id, nodes[i]);
            _ids.Add(nodes[i], id);
            Index(nodes[i].Children, id);
        }
    }

    private SectionNode Lazy(string label, string id, Func<IReadOnlyList<ViewNode>> load)
    {
        _lazy[id] = load;
        return SectionNode.Lazy(label, id);
    }

    private IReadOnlyList<ViewNode> TreePage(IReadOnlyList<GvasNode> nodes, int start, string group)
    {
        var result = new List<ViewNode>();
        foreach (var node in nodes.Skip(start).Take(PageSize))
        {
            var id = _ids[node];
            var label = $"{node.Name} · {node.Type}";
            if (node.Scalar is not null)
            {
                // Top-level fields live in Search so value ids are unique across the entire view.
                result.Add(_envelope.File.Properties.Contains(node)
                    ? new TextNode($"{label} (see Search for this value)")
                    : Bind(node));
            }
            else if (node.Children.Count > 0)
                result.Add(Lazy(label, id, () => TreePage(node.Children, 0, id)) with { Badge = node.Children.Count.ToString() });
            else
                result.Add(Lazy(label, id, () => [new TextNode(node.Note ?? "Opaque or empty property; preserved verbatim.") { Tone = Tone.Muted },
                    new HexNode(id, node.Length) { Title = label }]) with { Badge = $"{node.Length} bytes" });
        }
        if (start + PageSize < nodes.Count)
            result.Add(Lazy($"More ({start + PageSize}–{Math.Min(start + 2 * PageSize, nodes.Count) - 1})", $"{group}/page/{start + PageSize}",
                () => TreePage(nodes, start + PageSize, group)));
        return result;
    }

    private IReadOnlyList<ViewNode> SearchPage(IReadOnlyList<GvasNode> nodes, int start)
    {
        var rows = nodes.Skip(start).Take(PageSize).Select(node => new TableRow(_ids[node], new Dictionary<string, ViewNode>
        {
            ["name"] = new TextNode(node.Name), ["type"] = new TextNode(node.Type), ["value"] = Bind(node),
        })).ToArray();
        var result = new List<ViewNode> { new TableNode([new("name", "Property"), new("type", "Type"), new("value", "Value")], rows) };
        if (start + PageSize < nodes.Count)
            result.Add(Lazy("More primitive properties", $"search/page/{start + PageSize}", () => SearchPage(nodes, start + PageSize)));
        return result;
    }

    private FieldNode Bind(GvasNode node)
    {
        var scalar = node.Scalar!;
        return Field(_ids[node], $"{node.Name} · {node.Type}", Spec(scalar), () => ToFieldValue(scalar),
            CanEdit ? value => SetScalar(scalar, value) : null, value => Validate(scalar, value)) with
        {
            Confidence = Confidence.Verified,
            Description = "Raw serialized property; the name and storage type are confirmed by its tag.",
            Warning = _readOnlyReason ?? EditWarning,
        };
    }

    private static FieldSpec Spec(GvasScalar scalar) => scalar.Kind switch
    {
        GvasScalarKind.Boolean => new BoolSpec(),
        GvasScalarKind.String => new TextSpec { MaxLength = GvasFile.MaxStringLength - 1 },
        GvasScalarKind.Float => FloatSpec.Single,
        GvasScalarKind.Double => FloatSpec.Double,
        GvasScalarKind.Signed => new IntegerSpec(-(Int128.One << (scalar.Length * 8 - 1)), (Int128.One << (scalar.Length * 8 - 1)) - 1),
        _ => new IntegerSpec(0, (Int128.One << (scalar.Length * 8)) - 1),
    };

    private static FieldValue ToFieldValue(GvasScalar scalar) => scalar.Kind switch
    {
        GvasScalarKind.Boolean => FieldValue.Of((bool)scalar.Value),
        GvasScalarKind.String => FieldValue.Of((string)scalar.Value),
        GvasScalarKind.Float or GvasScalarKind.Double => FieldValue.Of((double)scalar.Value),
        _ => FieldValue.Of((Int128)scalar.Value),
    };

    private static string? Validate(GvasScalar scalar, FieldValue value)
    {
        try
        {
            var candidate = new GvasScalar(scalar.Kind, scalar.Offset, scalar.Length, scalar.Value);
            SetScalar(candidate, value);
            _ = candidate.Encode();
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidOperationException) { return ex.Message; }
    }

    private static void SetScalar(GvasScalar scalar, FieldValue value)
    {
        object next = scalar.Kind switch
        {
            GvasScalarKind.Boolean => value.AsBool(),
            GvasScalarKind.String => value.AsString(),
            GvasScalarKind.Float => (double)(float)value.AsDouble(),
            GvasScalarKind.Double => value.AsDouble(),
            _ => value.AsInteger(),
        };
        if (next is double number && !double.IsFinite(number)) throw new ArgumentException("Floating point value must be finite.");
        scalar.Value = next;
    }
}
