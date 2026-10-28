using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

internal static class InventoryIdentity
{
    public static string Describe(JsonValueToken entry)
    {
        var seed = entry.Member("ItemData")?.Member("GeneratorData")?.Member("GenesisRandomSeed")?.Scalar;
        if (seed is not { Kind: FieldValueKind.ExactNumber } number)
            return $"Session record {entry.Index}; generator seed absent (unknown persistent identity).";
        var raw = number.AsExactNumber();
        var occurrence = 0;
        foreach (var sibling in entry.Parent!.Children)
        {
            if (ReferenceEquals(sibling, entry)) break;
            if (sibling.Member("ItemData")?.Member("GeneratorData")?.Member("GenesisRandomSeed")?.Scalar == seed) occurrence++;
        }
        return $"Candidate identity seed={raw}, occurrence={occurrence}; stable session reference={entry.Index}. Seed persistence across PS5 saves is unverified.";
    }
}
