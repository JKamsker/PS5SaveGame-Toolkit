using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Attributed aliases over existing fixed-size primitives; references use exact decimal text.</summary>
internal sealed class ReferenceReadouts
{
    internal const string ItemsCatalog = "pragmata-items";
    internal const string ItemEvidenceCatalog = "pragmata-item-evidence";
    internal const string MissionsCatalog = "pragmata-missions";
    private readonly PragmataDocument _document;
    private readonly IReferenceContext _context;
    private readonly IReferenceCatalog? _items;
    private readonly IReferenceCatalog? _itemEvidence;
    private readonly IReferenceCatalog? _sections;
    private readonly IReferenceCatalog? _identifiers;
    private readonly IReferenceCatalog? _restoredIdentifiers;
    private readonly IReferenceCatalog? _missions;
    private readonly SchemaReadouts _schema;
    private readonly Dictionary<string, FieldValue> _readOnly = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (SaveNode Node, bool Reference)> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FieldMetadata> _metadata = new(StringComparer.Ordinal);

    public ReferenceReadouts(PragmataDocument document, IReferenceContext context)
    {
        _document = document;
        _context = context;
        _items = context.Catalog(ItemsCatalog);
        _itemEvidence = context.Catalog(ItemEvidenceCatalog);
        _sections = context.Catalog("pragmata-sections");
        _identifiers = context.Catalog("pragmata-identifiers");
        _restoredIdentifiers = context.Catalog("pragmata-restored-identifiers");
        _missions = context.Catalog(MissionsCatalog);
        _schema = new SchemaReadouts(context);
    }

    public ReferenceRecord? Item(uint id) => _items?.Find(id.ToString(CultureInfo.InvariantCulture));
    public ReferenceRecord? ItemEvidence(uint id) => _itemEvidence?.Find(id.ToString(CultureInfo.InvariantCulture));
    public ReferenceRecord? Section(uint hash) => _sections?.Find($"{hash:x8}");
    public string IdentifierSource(uint hash)
    {
        var record = _identifiers?.Find($"{hash:x8}") ?? _restoredIdentifiers?.Find($"{hash:x8}");
        return record is not null && record.Name == HashNames.All.GetValueOrDefault(hash) ? record.AttributionText : HashNames.Provenance(hash);
    }
    public string SchemaDescription(SaveNode node) => _schema.Describe(node, _document);
    public FieldMetadata? Metadata(string id) => _metadata.GetValueOrDefault(id);
    public ResolvedEdit? Resolve(string id, FieldValue value)
    {
        if (_readOnly.ContainsKey(id)) return new ResolvedEdit(null, value, "Reference evidence is browse-only.");
        if (!_bindings.TryGetValue(id, out var binding)) return null;
        if (binding.Reference)
        {
            if (value.Kind != FieldValueKind.Text || !uint.TryParse(value.AsString(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var raw))
                return new ResolvedEdit(binding.Node, value, "Item ID must be exact unsigned decimal uint32 text.");
            value = FieldValue.Of((ulong)raw);
        }
        return new ResolvedEdit(binding.Node, value);
    }
    public FieldValue? GetValue(string id) => _readOnly.TryGetValue(id, out var value) ? value :
        _bindings.TryGetValue(id, out var binding) ? Value(binding, _document.Read(binding.Node)) : null;
    public AssetContent? GetAsset(string id) => _context.GetAsset(id);
    public string AssetSource(string id) => _context.AssetProvenance(id)?.Attribution.ToShortText() ?? "Image source unavailable.";
    public IReferenceCatalog? Illustrations => _context.Catalog(IllustratedReferences.CatalogId);
    public MapNode WithAtlas(MapNode map) => new CommunityAtlas(_context).Attach(map);
    public IReferenceCatalog? Missions => _missions;

    public FieldNode Browse(string catalog, ReferenceRecord record)
    {
        var id = $"browse/{catalog}/{record.Key}";
        var value = FieldValue.Of(record.Key);
        _readOnly[id] = value;
        return new FieldNode(id, record.Name, new ReferenceSpec(catalog) { ReadOnly = true }, value)
        {
            Confidence = Confidence.Unknown,
            Description = $"{record.Description} {record.AttributionText} Reference catalogue only; this does not report saved ownership, completion or a legal limit.",
        };
    }

    public FieldNode ItemField(SaveNode node)
    {
        var raw = _document.Read(node).AsUInt32();
        var record = Item(raw);
        var id = $"inspect/item/{node.Path}";
        _bindings[id] = (node, true);
        if (!_metadata.TryGetValue(id, out var metadata))
        {
            metadata = new FieldMetadata(id, HashNames.Label(node.Hash),
                new ReferenceSpec(ItemsCatalog) { AllowCustom = true, Assess = candidate => AssessItem(node, candidate.Key) });
            _metadata.Add(id, metadata);
        }
        return new FieldNode(id, record?.Name ?? $"Unmapped item 0x{raw:x8}",
            metadata.Spec,
            FieldValue.Of(raw.ToString(CultureInfo.InvariantCulture)))
        {
            Confidence = record is null || record.Attributes.GetValueOrDefault("confidence") == "Unknown" ? Confidence.Unknown : Confidence.Inferred,
            Warning = PragmataDocument.EditWarning,
            Description = $"Raw ID 0x{raw:x8} / {raw}. {record?.Description ?? "No ID mapping available."} " +
                $"{record?.AttributionText ?? "Observed save value; no external name source."} Gameplay effects and legal limits are unverified. " +
                $"{EvidenceText(raw)} {_document.Bounds.Description(node)} Replacement changes only this stored ID; dependent references and unlock flags are not repaired. " +
                PragmataDocument.EditWarning,
        };
    }

    public FieldNode Primitive(SaveNode node)
    {
        if (_document.IsQuantity(node)) return _document.Field(node);
        var id = $"inspect/value/{node.Path}";
        _bindings[id] = (node, false);
        var metadata = new FieldMetadata(id, HashNames.Label(node.Hash), _document.Bounds.Spec(node));
        _metadata[id] = metadata;
        return new FieldNode(id, metadata.Label, metadata.Spec, _document.Read(node))
        {
            Confidence = Confidence.Unknown,
            Description = $"{node.Path} · {FieldTags.ShortName(node.Type)} @0x{node.Offset:x}. Identifier: {IdentifierSource(node.Hash)} " +
                $"{SchemaDescription(node)} " +
                $"{_document.Bounds.Description(node)} Bounds use matching typed fields in this save, including 0 and 1; game limits and dependencies remain unknown. {PragmataDocument.EditWarning}",
            Warning = PragmataDocument.EditWarning,
        };
    }

    public IReadOnlyList<ViewNode> ItemDetails(uint id)
    {
        var record = Item(id);
        var result = new List<ViewNode> { new TextNode($"Raw item ID: 0x{id:x8} / {id}"),
            new TextNode(record?.Description ?? "Player-facing mapping unavailable."),
            new TextNode($"Category: {record?.Category ?? "unknown"}; reference limit: {record?.Limit?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}. Reference limits are not proven game limits."),
            new TextNode(record?.AttributionText ?? "Source: observed save ID; no name dictionary.") };
        if (ItemEvidence(id) is { } evidence)
            result.Add(new TextNode($"Inferred source role: {evidence.Category}. {EvidenceText(id)}"));
        if (record?.AssetId is { } asset && _context.AssetProvenance(asset) is { } provenance)
        {
            result.Add(new ImageNode(asset, record.Name));
            result.Add(new TextNode($"Image source: {provenance.Attribution.ToShortText()}"));
        }
        return result;
    }

    private string EvidenceText(uint id) => ItemEvidence(id) is { } evidence
        ? $"{evidence.Description} Mission IDs: {evidence.Attributes.GetValueOrDefault("missionIds") ?? "Unknown"}. {evidence.AttributionText}"
        : "";

    private ReferenceEligibility AssessItem(SaveNode node, string key)
    {
        var error = uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var raw)
            ? _document.Bounds.Validate(node, FieldValue.Of((ulong)raw)) : "Expected an unsigned decimal uint32 item ID.";
        return error is null ? new ReferenceEligibility() : new ReferenceEligibility { Eligible = false, Reasons = [error] };
    }

    public IEnumerable<ValueChange> Changes(IReadOnlyDictionary<SaveNode, FieldValue> proposed) =>
        _bindings.Where(b => proposed.TryGetValue(b.Value.Node, out var next) && _document.Read(b.Value.Node) != next)
            .Select(b => new ValueChange(b.Key, Value(b.Value, _document.Read(b.Value.Node)), Value(b.Value, proposed[b.Value.Node])));

    private static FieldValue Value((SaveNode Node, bool Reference) binding, FieldValue value) =>
        binding.Reference ? FieldValue.Of(value.AsUInt32().ToString(CultureInfo.InvariantCulture)) : value;

    internal sealed record ResolvedEdit(SaveNode? Node, FieldValue Value, string? Error = null);
}
