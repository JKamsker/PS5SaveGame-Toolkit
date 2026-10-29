using System.Globalization;
using Xunit;

namespace SaveToolkit.Testing;

/// <summary>
/// Ordered samples from an allocation probe. Release assertions gate on the minimum sample;
/// Debug assertions only report samples and bounds to xUnit output, since budgets are Release-calibrated.
/// </summary>
public sealed class AllocationProbeResult
{
    internal AllocationProbeResult(long[] samples)
    {
        Samples = Array.AsReadOnly(samples);
        MinimumBytes = samples.Min();
    }

    public IReadOnlyList<long> Samples { get; }
    public long MinimumBytes { get; }

    /// <summary>Normalizes each sample (for example, bytes per operation) outside the measured interval.</summary>
    public AllocationProbeResult TransformSamples(Func<long, long> transform) => new(Samples.Select(transform).ToArray());

    public void AssertInRange(long minimum, long maximum, string? because = null) =>
        AssertBound(MinimumBytes >= minimum && MinimumBytes <= maximum,
            $"[{Format(minimum)}, {Format(maximum)}]", because);

    public void AssertAtMost(long maximum, string? because = null) => AssertInRange(0, maximum, because);

    public void AssertLessThan(long bound, string? because = null) =>
        AssertBound(MinimumBytes >= 0 && MinimumBytes < bound, $"< {Format(bound)}", because);

    public void AssertGreaterThan(long bound, string? because = null) =>
        AssertBound(MinimumBytes > bound, $"> {Format(bound)}", because);

    public override string ToString() => $"minimum {Format(MinimumBytes)} B; samples [{string.Join(", ", Samples.Select(Format))}] B";

    private void AssertBound(bool condition, string bound, string? because)
    {
        var message = Message(bound, because);
#if DEBUG
        TestContext.Current.TestOutputHelper?.WriteLine($"Debug allocation report (bounds enforced in Release only): {message}");
#else
        Assert.True(condition, message);
#endif
    }

    private string Message(string bound, string? because) => $"{because} Allocation bound: {bound} B; {this}.";
    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
