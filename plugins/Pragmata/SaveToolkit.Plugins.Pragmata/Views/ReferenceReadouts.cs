using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Read-only reference aliases stay isolated from the existing primitive editing bindings.</summary>
internal sealed class ReferenceReadouts
{
    internal const string ItemsCatalog = "pragmata-items";
    internal const string ItemEvidenceCatalog = "pragmata-item-evidence";
    private readonly PragmataDocument _document;
    private readonly IReferenceContext _context;
    private readonly IReferenceCatalog? _items;
    private readonly IReferenceCatalog? _itemEvidence;
    private readonly IReferenceCatalog? _sections;
    private readonly IReferenceCatalog? _identifiers;
    private readonly Dictionary<string, (SaveNode Node, bool Reference)> _bindings = new(StringComparer.Ordinal);

    public ReferenceReadouts(PragmataDocument document, IReferenceContext context)
    {
        _document = document;
        _context = context;
        _items = context.Catalog(ItemsCatalog);
        _itemEvidence = context.Catalog(ItemEvidenceCatalog);
        _sections = context.Catalog("pragmata-sections");
        _identifiers = context.Catalog("pragmata-identifiers");
    }

    public ReferenceRecord? Item(uint id) => _items?.Find(id.ToString(CultureInfo.InvariantCulture));
    public ReferenceRecord? ItemEvidence(uint id) => _itemEvidence?.Find(id.ToString(CultureInfo.InvariantCulture));
    public ReferenceRecord? Section(uint hash) => _sections?.Find($"{hash:x8}");
    public string IdentifierSource(uint hash)
    {
        var record = _identifiers?.Find($"{hash:x8}");
        return record is not null && record.Name == HashNames.All.GetValueOrDefault(hash) ? record.AttributionText : HashNames.Provenance(hash);
    }
    public bool IsReadOnly(string id) => _bindings.ContainsKey(id);
    public FieldValue? GetValue(string id) => _bindings.TryGetValue(id, out var binding) ? Value(binding, _document.Read(binding.Node)) : null;
    public AssetContent? GetAsset(string id) => _context.GetAsset(id);

    public FieldNode ItemField(SaveNode node)
    {
        var raw = _document.Read(node).AsUInt32();
        var record = Item(raw);
        var id = $"inspect/item/{node.Path}";
        _bindings[id] = (node, true);
        return new FieldNode(id, record?.Name ?? $"Unmapped item 0x{raw:x8}",
            new ReferenceSpec(ItemsCatalog) { ReadOnly = true, AllowCustom = false }, FieldValue.Of(raw.ToString(CultureInfo.InvariantCulture)))
        {
            Confidence = record is null || record.Attributes.GetValueOrDefault("confidence") == "Unknown" ? Confidence.Unknown : Confidence.Inferred,
            Description = $"Raw ID 0x{raw:x8} / {raw}. {record?.Description ?? "No ID mapping available."} " +
                $"{record?.AttributionText ?? "Observed save value; no external name source."} Gameplay effects and legal limits are unverified. " +
                EvidenceText(raw),
        };
    }

    public FieldNode Primitive(SaveNode node)
    {
        var id = $"inspect/value/{node.Path}";
        _bindings[id] = (node, false);
        return new FieldNode(id, HashNames.Label(node.Hash), PrimitiveCodec.Spec(node) with { ReadOnly = true }, _document.Read(node))
        {
            Confidence = Confidence.Unknown,
            Description = $"{node.Path} · {FieldTags.ShortName(node.Type)} @0x{node.Offset:x}. Identifier: {IdentifierSource(node.Hash)} Gameplay meaning unverified.",
        };
    }

    public IReadOnlyList<ViewNode> ItemDetails(uint id)
    {
        var record = Item(id);
        var result = new List<ViewNode> { new TextNode($"Raw item ID: 0x{id:x8} / {id}"),
            new TextNode(record?.Description ?? "Player-facing mapping unavailable."),
            new TextNode($"Category: {record?.Category ?? "unknown"}; legal limit: {record?.Limit?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}. Reference data does not authorize writes."),
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

    public IEnumerable<ValueChange> Changes(IReadOnlyDictionary<SaveNode, FieldValue> proposed) =>
        _bindings.Where(b => proposed.TryGetValue(b.Value.Node, out var next) && _document.Read(b.Value.Node) != next)
            .Select(b => new ValueChange(b.Key, Value(b.Value, _document.Read(b.Value.Node)), Value(b.Value, proposed[b.Value.Node])));

    private static FieldValue Value((SaveNode Node, bool Reference) binding, FieldValue value) =>
        binding.Reference ? FieldValue.Of(value.AsUInt32().ToString(CultureInfo.InvariantCulture)) : value;
}
