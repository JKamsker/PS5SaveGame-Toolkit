using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Editing;

/// <summary>Storage bindings and targeted quantity metadata, independent of rendered pages.</summary>
internal sealed class PragmataBindings(PragmataDocument document)
{
    internal const int MaximumMetadataBatchSize = 200;
    private const string RawPrefix = "raw/";
    private const string InspectPrefix = "inspect/value/";
    private static readonly string QuantitySuffix = $"/{KnownHashes.Quantity:x8}";
    private readonly Dictionary<string, SaveNode> _nodes = new(StringComparer.Ordinal);
    private readonly SaveNode? _items = document.Layout.Inventory?.Find(KnownHashes.Items);
    private Dictionary<string, SaveNode>? _quantities;

    public bool IsQuantity(SaveNode node) => _items is not null && node.Hash == KnownHashes.Quantity
        && node.Parent?.Parent == _items;

    public bool HasPrimary(SaveNode node) => _nodes.ContainsKey(node.Path);
    public SaveNode? Find(string id) => _nodes.GetValueOrDefault(id);
    public bool IsDerived(string id, SaveNode node) => IsQuantity(node) && id != node.Path;

    public string Register(SaveNode node, bool raw)
    {
        // All current quantity projections share one writable identity. Legacy prefixes are
        // available only through metadata as read-only aliases, never independent setters.
        var id = raw && !IsQuantity(node) ? RawPrefix + node.Path : node.Path;
        _nodes[id] = node;
        return id;
    }

    public FieldSpec Spec(SaveNode node)
    {
        var spec = document.Bounds.Spec(node);
        if (document.IsTimer(node) && spec is IntegerSpec timer)
            return timer with { Format = IntegerFormat.Duration, UnitsPerSecond = PragmataDocument.MicrosecondsPerSecond, Unit = "µs" };
        if (spec is IntegerSpec integer && HashNames.All.TryGetValue(node.Hash, out var name)
            && name.EndsWith("Hash", StringComparison.Ordinal))
            return integer with { Format = IntegerFormat.Hex };
        return spec;
    }

    public IReadOnlyList<FieldMetadata> Resolve(IReadOnlyList<string> valueIds)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(valueIds.Count, MaximumMetadataBatchSize);
        if (valueIds.Count == 0) return [];
        var result = new List<FieldMetadata>(valueIds.Count);
        foreach (var id in valueIds.Distinct(StringComparer.Ordinal))
        {
            if (document.References.Metadata(id) is { } reference)
            {
                result.Add(reference);
                continue;
            }
            var node = Find(id);
            if (node is not null && !IsQuantity(node))
            {
                result.Add(new FieldMetadata(id, HashNames.Label(node.Hash), Spec(node)));
                continue;
            }
            var path = id.StartsWith(RawPrefix, StringComparison.Ordinal) ? id[RawPrefix.Length..]
                : id.StartsWith(InspectPrefix, StringComparison.Ordinal) ? id[InspectPrefix.Length..] : id;
            if (!path.EndsWith(QuantitySuffix, StringComparison.Ordinal)) continue;
            // The writer never replaces the parsed fixed-size layout. Keep the lookup per document,
            // and lazy so opening a save does not format every unopened raw-tree path.
            _quantities ??= document.Layout.Items.Select(item => item.Find(KnownHashes.Quantity)!)
                .ToDictionary(quantity => quantity.Path, StringComparer.Ordinal);
            if (!_quantities.TryGetValue(path, out node)) continue;
            _nodes[node.Path] = node;
            _nodes[id] = node;
            var derived = id != node.Path;
            var spec = Spec(node);
            result.Add(new FieldMetadata(id, HashNames.Label(node.Hash), derived ? spec with { ReadOnly = true } : spec)
            {
                BindingKind = derived ? FieldBindingKind.Derived : FieldBindingKind.Primary,
                CanonicalValueId = node.Path,
            });
        }
        return result;
    }

    public IEnumerable<ValueChange> Changes(IReadOnlyDictionary<SaveNode, FieldValue> proposed) =>
        _nodes.Where(binding => proposed.TryGetValue(binding.Value, out var next) && document.Read(binding.Value) != next)
            .Select(binding => new ValueChange(binding.Key, document.Read(binding.Value), proposed[binding.Value]));
}
