using System.Text.Json;

namespace SaveToolkit.Sdk.Values;

/// <summary>Snapshots and compares published specs, including executable reference eligibility policy.</summary>
/// <remarks>Input collections must remain unchanged during each call. Snapshots retain delegates and their captured state;
/// they do not make callbacks thread-safe or freeze mutable closures.</remarks>
public static class FieldSpecMetadata
{
    /// <summary>Copies nested collections without losing reference-policy delegates through serialization.</summary>
    /// <exception cref="ArgumentNullException">The spec is null.</exception>
    public static FieldSpec Snapshot(FieldSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!TryEstimate(spec, long.MaxValue, out _))
            throw new InvalidOperationException("Field spec exceeds the bounded metadata snapshot limits.");
        return spec switch
        {
            EnumSpec choice => choice with { Options = Array.AsReadOnly(choice.Options.ToArray()) },
            FlagsSpec flags => flags with { Bits = Array.AsReadOnly(flags.Bits.ToArray()) },
            NullableSpec nullable => nullable with { Inner = Snapshot(nullable.Inner) },
            UnionSpec union => union with
            {
                Options = Array.AsReadOnly(union.Options.Select(option => option with { Spec = Snapshot(option.Spec) }).ToArray()),
            },
            // Leaf specs contain only immutable scalar values or delegates. Reusing the
            // instance avoids one record allocation per rendered field while retaining the
            // same immutable snapshot contract as the collection-bearing cases above.
            _ => spec,
        };
    }

    /// <summary>Estimates immutable spec storage without copying nested plugin-owned collections.</summary>
    /// <remarks>The estimate is conservative and bounded by the supplied remaining budget.</remarks>
    public static bool TryEstimate(FieldSpec spec, long limit, out long bytes)
    {
        ArgumentNullException.ThrowIfNull(spec);
        bytes = 0;
        if (limit < 0) return false;
        if (spec is not (NullableSpec or UnionSpec))
            return TryEstimateLeaf(spec, limit, ref bytes);

        var active = new HashSet<FieldSpec>(ReferenceEqualityComparer.Instance);
        var nodes = 0;
        return TryEstimateNode(spec, 0, active, ref nodes, ref bytes, limit);

        static bool TryEstimateNode(FieldSpec current, int depth, HashSet<FieldSpec> active,
            ref int nodes, ref long total, long maximum)
        {
            if (depth > FieldMetadataLimits.MaxSpecDepth
                || ++nodes > FieldMetadataLimits.MaxClosureIds
                || !active.Add(current)) return false;
            try
            {
                if (!AddString(ref total, current.Unit, maximum)) return false;
                switch (current)
                {
                    case EnumSpec choice:
                        if (choice.Options.Count > FieldMetadataLimits.MaxClosureIds) return false;
                        foreach (var option in choice.Options)
                            if (!Add(ref total, 40, maximum) || !AddString(ref total, option.Value, maximum)
                                || !AddString(ref total, option.Label, maximum)
                                || !AddString(ref total, option.Description, maximum)) return false;
                        return true;
                    case FlagsSpec flags:
                        if (flags.Bits.Count > FieldMetadataLimits.MaxClosureIds) return false;
                        foreach (var bit in flags.Bits)
                            if (!Add(ref total, 32, maximum) || !AddString(ref total, bit.Label, maximum)
                                || !AddString(ref total, bit.Description, maximum)) return false;
                        return true;
                    case ExactNumberSpec exact:
                        return AddString(ref total, exact.Min, maximum) && AddString(ref total, exact.Max, maximum);
                    case ReferenceSpec reference:
                        return AddString(ref total, reference.CatalogId, maximum);
                    case NullableSpec nullable:
                        return TryEstimateNode(nullable.Inner, depth + 1, active, ref nodes, ref total, maximum);
                    case UnionSpec union:
                        if (union.Options.Count > FieldMetadataLimits.MaxClosureIds) return false;
                        foreach (var option in union.Options)
                        {
                            if (!Add(ref total, 40, maximum) || !AddString(ref total, option.Id, maximum)
                                || !AddString(ref total, option.Label, maximum)
                                || !TryEstimateNode(option.Spec, depth + 1, active, ref nodes, ref total, maximum)) return false;
                        }
                        return true;
                    default:
                        return true;
                }
            }
            finally { active.Remove(current); }
        }

        static bool Add(ref long total, long value, long maximum)
        {
            if (value < 0 || total > maximum - value) return false;
            total += value;
            return true;
        }

        static bool AddString(ref long total, string? value, long maximum) =>
            value is null || (value.Length <= (maximum - total) / 2 && Add(ref total, 2L * value.Length, maximum));

        static bool TryEstimateLeaf(FieldSpec leaf, long maximum, ref long total)
        {
            if (!AddString(ref total, leaf.Unit, maximum)) return false;
            switch (leaf)
            {
                case EnumSpec choice:
                    if (choice.Options.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    foreach (var option in choice.Options)
                        if (!Add(ref total, 40, maximum) || !AddString(ref total, option.Value, maximum)
                            || !AddString(ref total, option.Label, maximum)
                            || !AddString(ref total, option.Description, maximum)) return false;
                    break;
                case FlagsSpec flags:
                    if (flags.Bits.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    foreach (var bit in flags.Bits)
                        if (!Add(ref total, 32, maximum) || !AddString(ref total, bit.Label, maximum)
                            || !AddString(ref total, bit.Description, maximum)) return false;
                    break;
                case ExactNumberSpec exact:
                    if (!AddString(ref total, exact.Min, maximum) || !AddString(ref total, exact.Max, maximum)) return false;
                    break;
                case ReferenceSpec reference:
                    if (!AddString(ref total, reference.CatalogId, maximum)) return false;
                    break;
            }

            return true;
        }
    }

    /// <summary>Compares all wire properties structurally and reference callbacks by delegate equality.</summary>
    /// <exception cref="ArgumentNullException">Either spec is null.</exception>
    public static bool Agree(FieldSpec first, FieldSpec second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return PoliciesAgree(first, second) && JsonSerializer.SerializeToUtf8Bytes(first).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(second));
    }

    private static bool PoliciesAgree(FieldSpec first, FieldSpec second) => (first, second) switch
    {
        (ReferenceSpec a, ReferenceSpec b) => a.Assess == b.Assess,
        (NullableSpec a, NullableSpec b) => PoliciesAgree(a.Inner, b.Inner),
        (UnionSpec a, UnionSpec b) => a.Options.Count == b.Options.Count
            && a.Options.Zip(b.Options).All(pair => PoliciesAgree(pair.First.Spec, pair.Second.Spec)),
        _ => first.GetType() == second.GetType(),
    };

}
