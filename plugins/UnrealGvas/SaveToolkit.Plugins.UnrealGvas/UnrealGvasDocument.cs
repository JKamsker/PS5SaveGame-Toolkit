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
    internal const string UnknownIntegrityWarning = "Not validated on console. Checksum coverage is unproven; the game may reject or overwrite this edited save. Keep a backup.";
    private readonly string _path;
    private readonly GvasEnvelope _envelope;
    private readonly UnrealGvasSchema _schema;
    private readonly IReferenceContext _references;
    private readonly CatalogueInspection _catalogues;
    private readonly JediActorStream? _actorStream;
    private readonly Dictionary<string, GvasNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<GvasNode, string> _ids = [];
    private readonly Dictionary<string, Func<IReadOnlyList<ViewNode>>> _lazy = new(StringComparer.Ordinal);
    private readonly string? _readOnlyReason;
    private readonly string? _integrityWarning;
    private const string RemoveLastPrefix = "gvas/structure/remove-last/";
    private sealed record Snapshot(byte[] Bytes);

    public UnrealGvasDocument(string path, GvasEnvelope envelope, IReferenceContext references, string? readOnlyReason = null)
    {
        _path = path;
        _envelope = envelope;
        _references = references;
        _schema = UnrealGvasSchema.Detect(envelope.File);
        var catalogueIds = new List<string> { UnrealGvasReferences.PropertyTypes };
        if (_schema.Kind == UnrealGvasSchemaKind.SackboyGeneral)
            catalogueIds.AddRange([UnrealGvasReferences.SackboyFields, UnrealGvasReferences.SackboyCostumes]);
        else if (_schema.Kind == UnrealGvasSchemaKind.SackboyImageSet)
            catalogueIds.Add(UnrealGvasReferences.SackboyImageFields);
        else if (_schema.Kind == UnrealGvasSchemaKind.JediGameplay)
            catalogueIds.Add(UnrealGvasReferences.JediFields);
        else if (_schema.Kind == UnrealGvasSchemaKind.JediSettings)
            catalogueIds.Add(UnrealGvasReferences.JediSettings);
        _catalogues = new(references, [.. catalogueIds]);
        Index(envelope.File.Properties, "property");
        _actorStream = ReadActorStream();
        if (readOnlyReason is not null) _readOnlyReason = readOnlyReason;
        else if (envelope.IsWrapped) _readOnlyReason = "This compressed envelope is read-only; recompression has not been verified.";
        else if (_schema.Kind == UnrealGvasSchemaKind.JediGameplay && _actorStream is not { IsValid: true })
            _readOnlyReason = "The actor payload failed bounded validation; the gameplay document remains read-only.";
        else if (_schema.Kind is UnrealGvasSchemaKind.SackboyGeneral or UnrealGvasSchemaKind.JediGameplay)
            _integrityWarning = UnknownIntegrityWarning;
        else if (_nodes.Values.Any(n => n.Name.Contains("crc", StringComparison.OrdinalIgnoreCase) ||
                 n.Name.Contains("checksum", StringComparison.OrdinalIgnoreCase) || n.Name.Contains("signature", StringComparison.OrdinalIgnoreCase)))
            _readOnlyReason = "This save contains a game-specific integrity field. Its coverage is unknown, so the entire document is read-only.";
    }

    public override string Title => _envelope.File.Header.SaveClass;
    public override bool CanEdit => _readOnlyReason is null;
    public override AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);
    internal JediActorStream? ActorStream => _actorStream;
    internal GvasFile CurrentFile => _envelope.File;

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
                new TextNode(_readOnlyReason ?? _integrityWarning ?? "Experimental. Embedded compressed payloads and unknown native structures are preserved read-only.") { Tone = Tone.Warning },
            ]),
            new Tab("Properties", [Lazy("Property tree", "tree", () => TreePage(file.Properties, 0, "tree"))]),
            new Tab("Search", [Lazy("Top-level primitive properties", "search", () => SearchPage(primitives, 0))]),
            new Tab("Catalogues", _catalogues.Build((id, catalogue, record) =>
                Field("property/" + id, catalogue.Dataset.Title ?? catalogue.Id, new ReferenceSpec(catalogue.Id), () => FieldValue.Of(record.Key)))),
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
            {
                var children = TreePage(node.Children, 0, id);
                if (CanRemoveLast(node)) children = [.. children, RemoveLastAction(id, node)];
                result.Add(Lazy(label, id, () => children) with { Badge = node.Children.Count.ToString() });
            }
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
        var actorLength = _schema.Kind == UnrealGvasSchemaKind.JediGameplay && node.Name == "UncompressedActorRecordsSize";
        var writable = CanEdit && !IsIntegrityNode(node) && !actorLength;
        return Field(_ids[node], $"{node.Name} · {node.Type}", GvasScalarField.Spec(scalar), () => GvasScalarField.ToFieldValue(scalar),
            writable ? value => GvasScalarField.SetScalar(scalar, value) : null, value => GvasScalarField.Validate(scalar, value)) with
        {
            Confidence = _integrityWarning is null ? Confidence.Verified : Confidence.Inferred,
            Description = _integrityWarning is null
                ? "Raw serialized property; the name and storage type are confirmed by its tag."
                : "Existing serialized field observed in supplied title captures; gameplay meaning and integrity coverage remain unproven.",
            Warning = actorLength ? "Read-only length of the preserved compressed actor stream. " + _integrityWarning
                : _readOnlyReason ?? _integrityWarning ?? EditWarning,
        };
    }

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (_catalogues.Invoke(actionId) is { } inspection) return inspection;
        if (!actionId.StartsWith(RemoveLastPrefix, StringComparison.Ordinal))
            return base.InvokeAction(actionId, parameters);
        if (!CanEdit) return new ActionOutcome { Message = _readOnlyReason ?? "Document is read-only." };
        if (_envelope.File.HasChanges) return new ActionOutcome { Message = "Save scalar edits before changing container structure." };
        string id = actionId[RemoveLastPrefix.Length..];
        if (!_nodes.TryGetValue(id, out var node) || !CanRemoveLast(node))
            return new ActionOutcome { Message = "The selected container is not a bounded primitive collection." };
        var outcome = Structural(() =>
        {
            var replacement = GvasStructuralEditor.RemoveLast(_envelope.File, node);
            var output = _envelope.File.SerializeReplacements(new Dictionary<GvasNode, ReadOnlyMemory<byte>> { [node] = replacement }, true);
            _envelope.ReplaceFile(GvasFile.Parse(output));
            Reindex();
        }, "Removed the last primitive container element. The operation is Experimental and reversible; game semantics are unverified.");
        return _integrityWarning is { } warning ? outcome with { Warnings = [warning] } : outcome;
    }

    protected override DocumentMemento? CaptureSnapshot()
    {
        if (_envelope.IsWrapped) return null;
        var bytes = _envelope.File.Serialize();
        return DocumentMemento.Snapshot(new Snapshot(bytes), bytes.LongLength);
    }

    protected override void LoadSnapshot(DocumentMemento snapshot)
    {
        if (snapshot is not SnapshotMemento<Snapshot> state) throw new InvalidOperationException("Invalid GVAS structural snapshot.");
        _envelope.ReplaceFile(GvasFile.Parse(state.State.Bytes));
        Reindex();
    }

    private void Reindex()
    {
        _nodes.Clear();
        _ids.Clear();
        _lazy.Clear();
        Index(_envelope.File.Properties, "property");
    }

    private static bool CanRemoveLast(GvasNode node)
    {
        var info = node.Container;
        return info is not null && node.Children.Count > 0 &&
            info.ElementType is not "StructProperty" &&
            (info.Kind != "MapProperty" || info.ValueType is not "StructProperty") &&
            node.Children.All(child => child.Scalar is not null);
    }

    private ActionNode RemoveLastAction(string id, GvasNode node) => new(RemoveLastPrefix + id, "Remove last element")
    {
        Description = $"Remove one {node.Container!.Kind} element while preserving all other bytes. Unknown game relationships are not inferred. {_integrityWarning ?? "Integrity metadata was not observed in this document."}",
        Confirm = "Remove the last element? This is Experimental and can change game semantics.",
        Tone = Tone.Warning,
    };

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var outcome = base.ApplyEdits(edits);
        return outcome.Succeeded && edits.Count > 0 && _integrityWarning is not null
            ? outcome with { Warnings = [_integrityWarning] }
            : outcome;
    }

    private static bool IsIntegrityNode(GvasNode node) =>
        node.Name.Contains("crc", StringComparison.OrdinalIgnoreCase) ||
        node.Name.Contains("checksum", StringComparison.OrdinalIgnoreCase) ||
        node.Name.Contains("signature", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<ViewNode> BuildSchemaOverlay() => new GvasSchemaViews(_schema, _references).Build(
        _envelope.File, _ids,
        (id, catalogue, name) => Field(id, "Property", new ReferenceSpec(catalogue), () => FieldValue.Of(name)),
        _schema.Kind != UnrealGvasSchemaKind.JediGameplay ? null :
            Lazy("Decoded actor stream", "jedi/actors", () => _actorStream is { IsValid: true }
                ? JediActorView.BuildSummary(_actorStream.Roots)
                : [new TextNode(_actorStream?.Error ?? "Actor stream was not found.") { Tone = Tone.Warning }]) with
            { Badge = _actorStream is { IsValid: true } valid ? $"{valid.DecodedLength:N0} bytes" : "opaque" });

    private JediActorStream? ReadActorStream()
    {
        if (_schema.Kind != UnrealGvasSchemaKind.JediGameplay) return null;
        var actors = _envelope.File.Properties.Where(node => node.Name == "CompressedActorRecords" && node.Type == "ArrayProperty").ToArray();
        var sizes = _envelope.File.Properties.Where(node => node.Name == "UncompressedActorRecordsSize" && node.Type == "UInt32Property").ToArray();
        if (actors.Length != 1 || sizes.Length != 1) return null;
        var actor = actors[0];
        var size = sizes[0].Scalar?.Value;
        if (size is null || !int.TryParse(size.ToString(), out var declared) || actor.Length < sizeof(int)) return null;
        var original = _envelope.File.Original.Span;
        if (actor.Offset < 0 || actor.Offset > original.Length - actor.Length) return null;
        var count = BitConverter.ToInt32(original.Slice(actor.Offset, sizeof(int)));
        if (count < 0 || count != actor.Length - sizeof(int)) return null;
        return JediActorStreamReader.Read(original.Slice(actor.Offset + sizeof(int), count), declared, _envelope.File.Header);
    }
}
