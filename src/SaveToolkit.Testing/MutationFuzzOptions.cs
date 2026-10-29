using System.Globalization;

namespace SaveToolkit.Testing;

/// <summary>Per-run overrides; an absent variable preserves the conformance class's defaults.</summary>
internal sealed record MutationFuzzOptions(uint Seed, int Mutations, TimeSpan? TimeBudget)
{
    internal const string SeedVariable = "SAVETOOLKIT_FUZZ_SEED";
    internal const string MutationsVariable = "SAVETOOLKIT_FUZZ_MUTATIONS";
    internal const string TimeBudgetVariable = "SAVETOOLKIT_FUZZ_TIME_BUDGET";

    internal static MutationFuzzOptions Read(uint defaultSeed, int defaultMutations,
        Func<string, string?>? readVariable = null)
    {
        readVariable ??= Environment.GetEnvironmentVariable;
        var seed = defaultSeed;
        if (readVariable(SeedVariable) is { } seedText)
        {
            var hex = seedText.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!uint.TryParse(hex ? seedText.AsSpan(2) : seedText.AsSpan(),
                hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                throw new ArgumentException($"{SeedVariable} must be an unsigned 32-bit decimal or 0x-prefixed hexadecimal integer.");
        }

        var mutations = ReadPositiveInteger(MutationsVariable, seed, readVariable) ?? defaultMutations;
        var seconds = ReadPositiveInteger(TimeBudgetVariable, seed, readVariable);
        return new(seed, mutations, seconds is { } value ? TimeSpan.FromSeconds(value) : null);
    }

    private static int? ReadPositiveInteger(string name, uint seed, Func<string, string?> readVariable)
    {
        if (readVariable(name) is not { } text) return null;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            throw new ArgumentException($"MutationFuzz seed=0x{seed:X8}: {name} must be an integer from 1 to {int.MaxValue}"
                + (name == TimeBudgetVariable ? " (seconds)." : "."));
        return value;
    }
}
