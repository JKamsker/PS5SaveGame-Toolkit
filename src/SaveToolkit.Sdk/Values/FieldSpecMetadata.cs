namespace SaveToolkit.Sdk.Values;

/// <summary>Snapshots and compares published specs, including executable reference eligibility policy.</summary>
/// <remarks>Input collections must remain unchanged during each call. Snapshots retain delegates and their captured state;
/// they do not make callbacks thread-safe or freeze mutable closures.</remarks>
public static class FieldSpecMetadata
{
    /// <summary>Copies nested collections without losing reference-policy delegates through serialization.</summary>
    /// <exception cref="ArgumentNullException">The spec is null.</exception>
    public static FieldSpec Snapshot(FieldSpec spec) => SnapshotBounded(spec, CancellationToken.None);

    /// <summary>Copies nested collections with cancellation-aware bounded traversal.</summary>
    public static FieldSpec SnapshotBounded(FieldSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!TryEstimate(spec, long.MaxValue, out _, cancellationToken))
            throw new InvalidOperationException("Field spec exceeds the bounded metadata snapshot limits.");
        return spec switch
        {
            EnumSpec choice => choice with { Options = Array.AsReadOnly(BoundedMetadataCollections.Copy(choice.Options, FieldMetadataLimits.MaxClosureIds, cancellationToken)) },
            FlagsSpec flags => flags with { Bits = Array.AsReadOnly(BoundedMetadataCollections.Copy(flags.Bits, FieldMetadataLimits.MaxClosureIds, cancellationToken)) },
            NullableSpec nullable => nullable with { Inner = SnapshotBounded(nullable.Inner, cancellationToken) },
            UnionSpec union => union with
            {
                Options = Array.AsReadOnly(BoundedMetadataCollections.Enumerate(union.Options, FieldMetadataLimits.MaxClosureIds, cancellationToken).Select(option => option with { Spec = SnapshotBounded(option.Spec, cancellationToken) }).ToArray()),
            },
            // Leaf specs contain only immutable scalar values or delegates. Reusing the
            // instance avoids one record allocation per rendered field while retaining the
            // same immutable snapshot contract as the collection-bearing cases above.
            _ => spec,
        };
    }

    /// <summary>Estimates immutable spec storage without copying nested plugin-owned collections.</summary>
    /// <remarks>The estimate is conservative and bounded by the supplied remaining budget.</remarks>
    public static bool TryEstimate(FieldSpec spec, long limit, out long bytes) =>
        TryEstimate(spec, limit, out bytes, CancellationToken.None);

    /// <summary>Estimates a finite spec graph while observing cancellation during nested traversal.</summary>
    public static bool TryEstimate(FieldSpec spec, long limit, out long bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        bytes = 0;
        cancellationToken.ThrowIfCancellationRequested();
        if (limit < 0) return false;
        try
        {
            if (spec is not (NullableSpec or UnionSpec))
                return TryEstimateLeaf(spec, limit, ref bytes, cancellationToken);

            var active = new HashSet<FieldSpec>(ReferenceEqualityComparer.Instance);
            var nodes = 0;
            return TryEstimateNode(spec, 0, active, ref nodes, ref bytes, limit, cancellationToken);
        }
        catch (InvalidOperationException) { return false; }

        static bool TryEstimateNode(FieldSpec current, int depth, HashSet<FieldSpec> active,
            ref int nodes, ref long total, long maximum, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                        foreach (var option in BoundedMetadataCollections.Enumerate(choice.Options, FieldMetadataLimits.MaxClosureIds, cancellationToken))
                            if (!Add(ref total, 40, maximum) || !AddString(ref total, option.Value, maximum)
                                || !AddString(ref total, option.Label, maximum)
                                || !AddString(ref total, option.Description, maximum)) return false;
                        return true;
                    case FlagsSpec flags:
                        if (flags.Bits.Count > FieldMetadataLimits.MaxClosureIds) return false;
                        foreach (var bit in BoundedMetadataCollections.Enumerate(flags.Bits, FieldMetadataLimits.MaxClosureIds, cancellationToken))
                            if (!Add(ref total, 32, maximum) || !AddString(ref total, bit.Label, maximum)
                                || !AddString(ref total, bit.Description, maximum)) return false;
                        return true;
                    case ExactNumberSpec exact:
                        return AddString(ref total, exact.Min, maximum) && AddString(ref total, exact.Max, maximum);
                    case ReferenceSpec reference:
                        return AddString(ref total, reference.CatalogId, maximum);
                    case NullableSpec nullable:
                        return TryEstimateNode(nullable.Inner, depth + 1, active, ref nodes, ref total, maximum, cancellationToken);
                    case UnionSpec union:
                        if (union.Options.Count > FieldMetadataLimits.MaxClosureIds) return false;
                        foreach (var option in BoundedMetadataCollections.Enumerate(union.Options, FieldMetadataLimits.MaxClosureIds, cancellationToken))
                        {
                            if (!Add(ref total, 40, maximum) || !AddString(ref total, option.Id, maximum)
                                || !AddString(ref total, option.Label, maximum)
                                || !TryEstimateNode(option.Spec, depth + 1, active, ref nodes, ref total, maximum, cancellationToken)) return false;
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

        static bool TryEstimateLeaf(FieldSpec leaf, long maximum, ref long total, CancellationToken cancellationToken)
        {
            if (!AddString(ref total, leaf.Unit, maximum)) return false;
            switch (leaf)
            {
                case EnumSpec choice:
                    if (choice.Options.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    foreach (var option in BoundedMetadataCollections.Enumerate(choice.Options, FieldMetadataLimits.MaxClosureIds, cancellationToken))
                        if (!Add(ref total, 40, maximum) || !AddString(ref total, option.Value, maximum)
                            || !AddString(ref total, option.Label, maximum)
                            || !AddString(ref total, option.Description, maximum)) return false;
                    break;
                case FlagsSpec flags:
                    if (flags.Bits.Count > FieldMetadataLimits.MaxClosureIds) return false;
                    foreach (var bit in BoundedMetadataCollections.Enumerate(flags.Bits, FieldMetadataLimits.MaxClosureIds, cancellationToken))
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
        if (!TryEstimate(first, long.MaxValue, out _) || !TryEstimate(second, long.MaxValue, out _))
            throw new InvalidOperationException("Field spec exceeds bounded comparison limits.");
        return StructuralEquals(first, second);
    }

    private static bool StructuralEquals(FieldSpec first, FieldSpec second) => (first, second) switch
    {
        (NullableSpec a, NullableSpec b) => a with { Inner = ComparisonLeaf } == b with { Inner = ComparisonLeaf }
            && StructuralEquals(a.Inner, b.Inner),
        (UnionSpec a, UnionSpec b) => a with { Options = EmptyUnion } == b with { Options = EmptyUnion }
            && a.Options.Count == b.Options.Count && a.Options.Zip(b.Options).All(pair =>
                pair.First.Id == pair.Second.Id && pair.First.Label == pair.Second.Label
                && StructuralEquals(pair.First.Spec, pair.Second.Spec)),
        (EnumSpec a, EnumSpec b) => a with { Options = EmptyEnum } == b with { Options = EmptyEnum }
            && a.Options.SequenceEqual(b.Options),
        (FlagsSpec a, FlagsSpec b) => a with { Bits = EmptyFlags } == b with { Bits = EmptyFlags }
            && a.Bits.SequenceEqual(b.Bits),
        _ => first == second,
    };

    private static readonly FieldSpec ComparisonLeaf = new BoolSpec();
    private static readonly IReadOnlyList<UnionOption> EmptyUnion = Array.Empty<UnionOption>();
    private static readonly IReadOnlyList<EnumOption> EmptyEnum = Array.Empty<EnumOption>();
    private static readonly IReadOnlyList<FlagBit> EmptyFlags = Array.Empty<FlagBit>();
}
