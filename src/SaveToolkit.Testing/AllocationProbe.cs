namespace SaveToolkit.Testing;

/// <summary>Measures synchronous, current-thread managed allocations after warming the same operation.</summary>
public static class AllocationProbe
{
    public const int DefaultMeasuredRuns = 3;

    /// <summary>
    /// Returns every measured sample and its minimum. Setup and cleanup run outside the counter
    /// interval for every invocation, including warm-ups. Use them to reset mutable state or
    /// create/dispose fresh documents when measuring a first-use operation. Delegates must not
    /// dispatch work to other threads. Background/native allocations are not measured.
    /// </summary>
    public static AllocationProbeResult Measure(Action operation, int warmupRuns = 1,
        int measuredRuns = DefaultMeasuredRuns, bool collectBetweenRuns = false,
        Action? beforeRun = null, Action? afterRun = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfLessThan(warmupRuns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(measuredRuns, 1);
        var samples = new long[measuredRuns];
        for (var run = 0; run < warmupRuns; run++) Run(measure: false);
        for (var run = 0; run < measuredRuns; run++) samples[run] = Run(measure: true);
        return new(samples);

        long Run(bool measure)
        {
            if (collectBetweenRuns)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            try
            {
                beforeRun?.Invoke();
                var before = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
                operation();
                return measure ? GC.GetAllocatedBytesForCurrentThread() - before : 0;
            }
            finally
            {
                afterRun?.Invoke();
            }
        }
    }

    /// <summary>Also returns the last measured operation's result, independently of the minimum sample.</summary>
    public static AllocationProbeResult Measure<T>(Func<T> operation, out T result, int warmupRuns = 1,
        int measuredRuns = DefaultMeasuredRuns, bool collectBetweenRuns = false,
        Action? beforeRun = null, Action? afterRun = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        T last = default!;
        var measurement = Measure(() => { last = operation(); }, warmupRuns, measuredRuns,
            collectBetweenRuns, beforeRun, afterRun);
        result = last;
        return measurement;
    }

    /// <summary>
    /// Measures opening a fresh disposable result, optionally including its first view. Disposes
    /// previous results outside the interval; the caller owns the final result on success.
    /// A failed open or first view disposes the current result before propagating the exception.
    /// </summary>
    public static AllocationProbeResult MeasureOpen<T>(Func<T> open, out T result, Action<T>? firstView = null)
        where T : class, IDisposable
    {
        ArgumentNullException.ThrowIfNull(open);
        T? current = null;
        try
        {
            var measurement = Measure(() =>
            {
                current = open();
                firstView?.Invoke(current);
            }, beforeRun: () =>
            {
                var previous = current;
                current = null;
                previous?.Dispose();
            });
            result = current!;
            return measurement;
        }
        catch
        {
            current?.Dispose();
            throw;
        }
    }
}
