using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Editing;

/// <summary>Freezes conservative editor bounds at open; these are not claimed game limits.</summary>
internal sealed class ObservedEditBounds
{
    private readonly Dictionary<Key, FieldSpec> _specs = [];

    public ObservedEditBounds(SaveLayout layout, byte[] original)
    {
        // Header, clocks, quantities and system/raw metadata retain their existing storage bounds.
        // Other gameplay primitives back the newly enabled family controls and their raw aliases.
        foreach (var section in layout.Sections.Where(section => section != layout.Clock))
            foreach (var node in Descendants(section).Where(node => node.IsPrimitive))
            {
                if (section == layout.Inventory && node.Hash == KnownHashes.Quantity) continue;
                var value = PrimitiveCodec.Read(node, original);
                var key = Key.For(node);
                var current = _specs.GetValueOrDefault(key) ?? PrimitiveCodec.Spec(node);
                if (current is IntegerSpec integer)
                {
                    var min = Int128.Min(0, value.AsInteger());
                    var max = Int128.Max(1, value.AsInteger());
                    _specs[key] = _specs.ContainsKey(key)
                        ? integer with { Min = Int128.Min(integer.Min, min), Max = Int128.Max(integer.Max, max) }
                        : integer with { Min = min, Max = max };
                }
                else if (current is FloatSpec floating && double.IsFinite(value.AsDouble()))
                    _specs[key] = floating with
                    {
                        Min = Math.Min(floating.Min ?? 0, value.AsDouble()),
                        Max = Math.Max(floating.Max ?? 1, value.AsDouble()),
                    };
            }
    }

    public FieldSpec Spec(SaveNode node) => _specs.GetValueOrDefault(Key.For(node)) ?? PrimitiveCodec.Spec(node);

    public string Description(SaveNode node) => Spec(node) switch
    {
        IntegerSpec integer => $"Editor range {integer.Min}..{integer.Max}.",
        FloatSpec floating => FormattableString.Invariant($"Editor range {floating.Min}..{floating.Max}."),
        _ => "Stored boolean only.",
    };

    public string? Validate(SaveNode node, FieldValue value)
    {
        if (PrimitiveCodec.Validate(node, value) is { } error) return error;
        return Spec(node) switch
        {
            IntegerSpec integer when value.AsInteger() < integer.Min || value.AsInteger() > integer.Max =>
                "Outside conservative editor bounds from this save's matching typed fields (including 0 and 1); game limits are unknown.",
            FloatSpec floating when value.AsDouble() < floating.Min || value.AsDouble() > floating.Max =>
                "Outside conservative editor bounds from this save's matching typed fields (including 0 and 1); game limits are unknown.",
            _ => null,
        };
    }

    private static IEnumerable<SaveNode> Descendants(SaveNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    // Match names only within the same containing stored class/array and primitive representation.
    private readonly record struct Key(uint Parent, uint Hash, int Type, int Size)
    {
        public static Key For(SaveNode node) => new(node.Parent?.ClassHash is > 0 ? node.Parent.ClassHash : node.Parent?.Hash ?? 0,
            node.Hash, node.Type, node.Size);
    }
}
