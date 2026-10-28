using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

internal sealed class UnrealGvasDocument : BoundSaveDocument
{
    private const int PageSize = 100;
    private const string EditWarning = "Serialized property name and type are verified. Game constraints and effects are unverified; keep a backup.";
    private readonly string _path;
    private readonly GvasEnvelope _envelope;
    private readonly UnrealGvasSchema _schema;
    private readonly IReferenceContext _references;
    private readonly JediActorStream? _actorStream;
    private readonly Dictionary<string, GvasNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<GvasNode, string> _ids = [];
    private readonly Dictionary<string, Func<IReadOnlyList<ViewNode>>> _lazy = new(StringComparer.Ordinal);
    private readonly string? _readOnlyReason;

    public UnrealGvasDocument(string path, GvasEnvelope envelope, IReferenceContext references, string? readOnlyReason = null)
    {
        _path = path;
        _envelope = envelope;
        _references = references;
        _schema = UnrealGvasSchema.Detect(envelope.File);
        Index(envelope.File.Properties, "property");
        _actorStream = ReadActorStream();
        if (readOnlyReason is not null) _readOnlyReason = readOnlyReason;
        else if (envelope.IsWrapped) _readOnlyReason = "This compressed envelope is read-only; recompression has not been verified.";
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
        var tabs = new List<Tab>
        {
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
                new TextNode($"Schema: {_schema.Name} ({_schema.Confidence}). {_schema.Evidence}") { Tone = _schema.IsTitleSchema ? Tone.Info : Tone.Muted },
                .. ImageSetNotes(header.SaveClass),
                new TextNode(_readOnlyReason ?? "Experimental. Embedded compressed payloads and unknown native structures are preserved read-only.") { Tone = Tone.Warning },
            ]),
            new Tab("Properties", [Lazy("Property tree", "tree", () => TreePage(file.Properties, 0, "tree"))]),
            new Tab("Search", [Lazy("Top-level primitive properties", "search", () => SearchPage(primitives, 0))]),
        };
        if (_schema.IsTitleSchema) tabs.Add(new Tab("Schema", BuildSchemaOverlay()));
        return new TabsNode(tabs);
    }

    private static IReadOnlyList<ViewNode> ImageSetNotes(string saveClass) =>
        saveClass == "/Script/GingerBread.GingerbreadSaveImageSet"
        ? [new TextNode("Sackboy image set: tagged GVAS containing Unreal chunk-compressed texture bytes. The supplied images are not PNG/JPEG assets; raw records remain available in Properties.") { Tone = Tone.Info }]
        : [];

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

    private IReadOnlyList<ViewNode> BuildSchemaOverlay()
    {
        var nodes = GvasFile.Descendants(_envelope.File.Properties).ToArray();
        var result = new List<ViewNode>
        {
            new StatsNode([
                new Stat("Schema", _schema.Name),
                new Stat("Build", _schema.Build),
                new Stat("Evidence", _schema.Confidence),
            ]),
            new TextNode(_schema.Evidence) { Tone = Tone.Info },
        };
        if (_schema.Kind == UnrealGvasSchemaKind.SackboyGeneral)
            result.Add(BuildNamedPropertyTable(nodes, UnrealGvasReferences.SackboyFields,
                ["CollectabellsStored", "TotalCollectabellsCollected", "UnlockedWorlds", "Checksum"],
                "Verified storage fields; checksum coverage and gameplay effects are unknown."));
        else if (_schema.Kind == UnrealGvasSchemaKind.SackboyImageSet)
            result.Add(BuildNamedPropertyTable(nodes, UnrealGvasReferences.SackboyImageFields,
                ["SurfaceWidth", "SurfaceHeight", "PixelFormat", "Image"],
                "Metadata is readable; PS5 channel order, swizzle and screenshot correspondence remain inferred."));
        else if (_schema.Kind == UnrealGvasSchemaKind.JediGameplay)
        {
            result.Add(BuildNamedPropertyTable(nodes, UnrealGvasReferences.JediFields,
                ["CompressedActorRecords", "UncompressedActorRecordsSize", "Crc32"],
                "The complete document stays read-only until checksum coverage is established."));
            result.Add(Lazy("Decoded actor stream", "jedi/actors", () => _actorStream is { IsValid: true }
                ? BuildActorPage(_actorStream.Roots, 0)
                : [new TextNode(_actorStream?.Error ?? "Actor stream was not found.") { Tone = Tone.Warning }]) with
            { Badge = _actorStream is { IsValid: true } valid ? $"{valid.DecodedLength:N0} bytes" : "opaque" });
        }
        else if (_schema.Kind == UnrealGvasSchemaKind.HogwartsMetadata)
            result.Add(new TextNode("Database tables are exposed in the Hogwarts tabs through the shared Kraken/SQLite codecs. Raw arrays and metadata remain read-only.") { Tone = Tone.Info });
        return result;
    }

    private TableNode BuildNamedPropertyTable(IReadOnlyList<GvasNode> nodes, string catalogId,
        IReadOnlyList<string> names, string description)
    {
        var rows = names.Select(name =>
        {
            var node = nodes.LastOrDefault(candidate => candidate.Name == name);
            var raw = node?.Scalar is { } scalar ? scalar.Value.ToString() ?? "" : node is null ? "Not present" : "Opaque";
            var record = _references.Catalog(catalogId)?.Find(name);
            var reference = Field($"schema/{catalogId}/{name}", "Property",
                new ReferenceSpec(catalogId), () => FieldValue.Of(name))
            with
            {
                Confidence = record is null ? Confidence.Unknown : Confidence.Verified,
                Description = record?.AttributionText ?? "Raw serialized property name; no pinned catalog record is available.",
                Warning = "Read-only semantic overlay. The raw property remains the source of truth.",
            };
            return new TableRow(name, new Dictionary<string, ViewNode>
            {
                ["property"] = reference,
                ["value"] = new TextNode(raw) { Monospace = node is null },
                ["type"] = new TextNode(node?.Type ?? "Missing") { Tone = node is null ? Tone.Warning : Tone.Normal },
            });
        }).ToArray();
        return new TableNode([new("property", "Catalog property"), new("type", "Storage type"), new("value", "Current value")], rows)
        {
            Title = "Observed schema fields",
            Description = description + " Catalog attribution is shown by each field.",
        };
    }

    private IReadOnlyList<ViewNode> BuildActorPage(IReadOnlyList<GvasNode> roots, int start)
    {
        var flat = Flatten(roots);
        var rows = flat.Skip(start).Take(PageSize).Select(item => new TableRow(item.Path, new Dictionary<string, ViewNode>
        {
            ["path"] = new TextNode(item.Path) { Monospace = true },
            ["type"] = new TextNode(item.Node.Type),
            ["value"] = new TextNode(item.Node.Scalar?.Value.ToString() ?? item.Node.Note ?? $"{item.Node.Length:N0} bytes")
        })).ToArray();
        var result = new List<ViewNode>
        {
            new TextNode("Headerless actor properties are decoded with the same bounded legacy GVAS tag reader. Names and storage types are evidence; actor roles, currency, XP and skill semantics are not inferred.") { Tone = Tone.Warning },
            new TableNode([new("path", "Path"), new("type", "Type"), new("value", "Value")], rows)
            { Title = "Actor property index", Description = "Paged at 100 rows; unsupported structs remain opaque." },
        };
        if (start + PageSize < flat.Count)
            result.Add(Lazy("More actor properties", $"jedi/actors/{start + PageSize}", () => BuildActorPage(roots, start + PageSize)));
        return result;
    }

    private static List<(string Path, GvasNode Node)> Flatten(IReadOnlyList<GvasNode> roots)
    {
        var result = new List<(string, GvasNode)>();
        void Visit(IReadOnlyList<GvasNode> current, string prefix)
        {
            for (var i = 0; i < current.Count; i++)
            {
                var node = current[i];
                var path = $"{prefix}/{i}:{node.Name}";
                result.Add((path, node));
                if (node.Children.Count > 0) Visit(node.Children, path);
            }
        }
        Visit(roots, "actors");
        return result;
    }

    private JediActorStream? ReadActorStream()
    {
        if (_schema.Kind != UnrealGvasSchemaKind.JediGameplay) return null;
        var actor = GvasFile.Descendants(_envelope.File.Properties).FirstOrDefault(node => node.Name == "CompressedActorRecords");
        var size = GvasFile.Descendants(_envelope.File.Properties).FirstOrDefault(node => node.Name == "UncompressedActorRecordsSize")?.Scalar?.Value;
        if (actor is null || size is null || !int.TryParse(size.ToString(), out var declared) || actor.Length < sizeof(int)) return null;
        var original = _envelope.File.Original.Span;
        if (actor.Offset < 0 || actor.Offset > original.Length - actor.Length) return null;
        var count = BitConverter.ToInt32(original.Slice(actor.Offset, sizeof(int)));
        if (count < 0 || count > actor.Length - sizeof(int)) return null;
        return JediActorStreamReader.Read(original.Slice(actor.Offset + sizeof(int), count), declared, _envelope.File.Header);
    }

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
