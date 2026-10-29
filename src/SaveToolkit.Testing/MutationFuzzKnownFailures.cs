namespace SaveToolkit.Testing;

/// <summary>Central quarantine because plugin folders are outside the scope of harness work (#40).</summary>
internal static class MutationFuzzKnownFailures
{
    internal static IReadOnlyList<ExpectedFuzzFailure> For(string pluginId) => pluginId switch
    {
        _ => [],
    };
}
