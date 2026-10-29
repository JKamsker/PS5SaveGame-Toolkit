using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class InventoryIdentity(IReadOnlyList<JsonValueToken> entries)
{
    private Dictionary<int, int>? _occurrences;

    public string Describe(JsonValueToken entry)
    {
        var seed = entry.Member("ItemData")?.Member("GeneratorData")?.Member("GenesisRandomSeed")?.Scalar;
        if (seed is not { Kind: FieldValueKind.ExactNumber } number)
            return $"Session record {entry.Index}; generator seed absent (unknown persistent identity).";
        var raw = number.AsExactNumber();
        var occurrence = (_occurrences ??= CountOccurrences())[entry.Index];
        return $"Candidate identity seed={raw}, occurrence={occurrence}; stable session reference={entry.Index}. Seed persistence across PS5 saves is unverified.";
    }

    private Dictionary<int, int> CountOccurrences()
    {
        var counts = new Dictionary<FieldValue, int>();
        var occurrences = new Dictionary<int, int>();
        foreach (var entry in entries)
        {
            var seed = entry.Member("ItemData")?.Member("GeneratorData")?.Member("GenesisRandomSeed")?.Scalar;
            if (seed is not { Kind: FieldValueKind.ExactNumber } number) continue;
            var occurrence = counts.GetValueOrDefault(number);
            occurrences.Add(entry.Index, occurrence);
            counts[number] = occurrence + 1;
        }
        return occurrences;
    }
}
