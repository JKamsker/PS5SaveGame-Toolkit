using System.Text.Json;

namespace SaveToolkit.Sdk.Values;

/// <summary>Snapshots and compares published specs, including executable reference eligibility policy.</summary>
/// <remarks>Input collections must remain unchanged during each call. Snapshots retain delegates and their captured state;
/// they do not make callbacks thread-safe or freeze mutable closures.</remarks>
public static class FieldSpecMetadata
{
    private const long SpecNodeOverheadBytes = 64;
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

        var pending = new Stack<SpecFrame>();
        pending.Push(new(spec, 0));
        var nodes = 0;
        while (pending.Count > 0)
        {
            var frame = pending.Pop();
            if (frame.Depth > FieldMetadataLimits.MaxSpecDepth || ++nodes > FieldMetadataLimits.MaxClosureIds)
                return false;

            if (frame.NextUnionOption > 0 && frame.Spec is UnionSpec continuation)
            {
                var index = frame.NextUnionOption - 1;
                if (index < continuation.Options.Count)
                {
                    var option = continuation.Options[index];
                    if (!Add(ref bytes, 40, limit) || !AddString(ref bytes, option.Id, limit)
                        || !AddString(ref bytes, option.Label, limit)) return false;
                    pending.Push(frame with { NextUnionOption = frame.NextUnionOption + 1 });
                    pending.Push(new(option.Spec, frame.Depth + 1));
                }
                continue;
            }

            if (frame.Spec is NullableSpec or UnionSpec
                && !Add(ref bytes, SpecNodeOverheadBytes, limit)) return false;
            if (!AddString(ref bytes, frame.Spec.Unit, limit)) return false;
            switch (frame.Spec)
            {
                case EnumSpec choice:
                    if (choice.Options.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    foreach (var option in choice.Options)
                    {
                        if (!Add(ref bytes, 40, limit) || !AddString(ref bytes, option.Value, limit)
                            || !AddString(ref bytes, option.Label, limit)
                            || !AddString(ref bytes, option.Description, limit)) return false;
                    }
                    break;
                case FlagsSpec flags:
                    if (flags.Bits.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    foreach (var bit in flags.Bits)
                    {
                        if (!Add(ref bytes, 32, limit) || !AddString(ref bytes, bit.Label, limit)
                            || !AddString(ref bytes, bit.Description, limit)) return false;
                    }
                    break;
                case ExactNumberSpec exact:
                    if (!AddString(ref bytes, exact.Min, limit) || !AddString(ref bytes, exact.Max, limit)) return false;
                    break;
                case ReferenceSpec reference:
                    if (!AddString(ref bytes, reference.CatalogId, limit)) return false;
                    break;
                case NullableSpec nullable:
                    pending.Push(new(nullable.Inner, frame.Depth + 1));
                    break;
                case UnionSpec union:
                    if (union.Options.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    if (union.Options.Count > 0)
                        pending.Push(new(union, frame.Depth, 1));
                    break;
            }
        }

        return true;

        static bool TryEstimateLeaf(FieldSpec leaf, long maximum, ref long total)
        {
            if (!AddString(ref total, leaf.Unit, maximum)) return false;
            switch (leaf)
            {
                case EnumSpec choice:
                    if (choice.Options.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    foreach (var option in choice.Options)
                    {
                        if (!Add(ref total, 40, maximum) || !AddString(ref total, option.Value, maximum)
                            || !AddString(ref total, option.Label, maximum)
                            || !AddString(ref total, option.Description, maximum)) return false;
                    }
                    break;
                case FlagsSpec flags:
                    if (flags.Bits.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    foreach (var bit in flags.Bits)
                    {
                        if (!Add(ref total, 32, maximum) || !AddString(ref total, bit.Label, maximum)
                            || !AddString(ref total, bit.Description, maximum)) return false;
                    }
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

        static bool Add(ref long total, long value, long maximum)
        {
            if (value < 0 || total > maximum - value) return false;
            total += value;
            return true;
        }

        static bool AddString(ref long total, string? value, long maximum) =>
            value is null || (value.Length <= (maximum - total) / 2 && Add(ref total, 2L * value.Length, maximum));
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

    private readonly record struct SpecFrame(FieldSpec Spec, int Depth, int NextUnionOption = 0);
}
