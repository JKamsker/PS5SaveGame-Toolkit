using System.Text.Json;

namespace SaveToolkit.Sdk.Values;

/// <summary>Snapshots and compares published specs, including executable reference eligibility policy.</summary>
public static class FieldSpecMetadata
{
    /// <summary>Copies nested collections without losing reference-policy delegates through serialization.</summary>
    public static FieldSpec Snapshot(FieldSpec spec) => spec switch
    {
        EnumSpec choice => choice with { Options = Array.AsReadOnly(choice.Options.ToArray()) },
        FlagsSpec flags => flags with { Bits = Array.AsReadOnly(flags.Bits.ToArray()) },
        NullableSpec nullable => nullable with { Inner = Snapshot(nullable.Inner) },
        UnionSpec union => union with
        {
            Options = Array.AsReadOnly(union.Options.Select(option => option with { Spec = Snapshot(option.Spec) }).ToArray()),
        },
        _ => spec with { },
    };

    /// <summary>Compares all wire properties structurally and reference callbacks by delegate equality.</summary>
    public static bool Agree(FieldSpec first, FieldSpec second) =>
        PoliciesAgree(first, second) && JsonSerializer.SerializeToUtf8Bytes(first).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(second));

    private static bool PoliciesAgree(FieldSpec first, FieldSpec second) => (first, second) switch
    {
        (ReferenceSpec a, ReferenceSpec b) => a.Assess == b.Assess,
        (NullableSpec a, NullableSpec b) => PoliciesAgree(a.Inner, b.Inner),
        (UnionSpec a, UnionSpec b) => a.Options.Count == b.Options.Count
            && a.Options.Zip(b.Options).All(pair => PoliciesAgree(pair.First.Spec, pair.Second.Spec)),
        _ => first.GetType() == second.GetType(),
    };
}
