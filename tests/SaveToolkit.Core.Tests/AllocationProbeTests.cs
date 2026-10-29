using System.Globalization;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class AllocationProbeTests
{
    private static readonly string[] CallbackOrder = ["setup", "operation", "cleanup"];

    [Fact]
    public void DefaultsWarmUpOnceThenKeepThreeOrderedSamplesAndTheMinimum()
    {
        var calls = 0;
        int[] lengths = [8_000, 3_000, 1_000, 2_000];

        var result = AllocationProbe.Measure(() => GC.KeepAlive(new byte[lengths[calls++]]));

        Assert.Equal(4, calls);
        Assert.Equal(3, result.Samples.Count);
        Assert.Contains(result.MinimumBytes, result.Samples);
        for (var index = 0; index < result.Samples.Count; index++)
        {
            // Runtime work can add bytes, so do not assume an exact allocation size or ordering.
            Assert.True(result.Samples[index] >= lengths[index + 1]);
            Assert.True(result.MinimumBytes <= result.Samples[index]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllCallbacksRunSynchronouslyOnTheCallingThreadWithConfigurableRunCounts(bool collect)
    {
        var thread = Environment.CurrentManagedThreadId;
        var events = new List<string>();
        var result = AllocationProbe.Measure(() => Record("operation"), warmupRuns: 2, measuredRuns: 4,
            collectBetweenRuns: collect, beforeRun: () => Record("setup"), afterRun: () => Record("cleanup"));

        Assert.Equal(4, result.Samples.Count);
        Assert.Equal(Enumerable.Range(0, 6).SelectMany(_ => CallbackOrder), events);

        void Record(string name)
        {
            Assert.Equal(thread, Environment.CurrentManagedThreadId);
            events.Add(name);
        }
    }

    [Fact]
    public void SetupCleanupAndResultBookkeepingAreOutsideTheMeasuredInterval()
    {
        var result = AllocationProbe.Measure(() => { },
            beforeRun: () => GC.KeepAlive(new byte[10_000]), afterRun: () => GC.KeepAlive(new byte[10_000]));

        result.AssertAtMost(0);
    }

    [Fact]
    public void GenericOverloadReturnsTheLastResultWithoutReexecutingTheOperation()
    {
        var calls = 0;
        var result = AllocationProbe.Measure(() => ++calls, out var value);

        Assert.Equal(4, calls);
        Assert.Equal(4, value);
        Assert.Equal(3, result.Samples.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExceptionsPropagateAndCleanupStillRuns(bool setupThrows)
    {
        var expected = new InvalidOperationException("expected");
        var cleanupCalls = 0;
        var error = Assert.Throws<InvalidOperationException>(() => AllocationProbe.Measure(
            () => throw expected, beforeRun: () => { if (setupThrows) throw expected; },
            afterRun: () => cleanupCalls++));

        Assert.Same(expected, error);
        Assert.Equal(1, cleanupCalls);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void InvalidRunCountsAreRejectedBeforeInvokingCallbacks(int warmups, int measurements)
    {
        var calls = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => AllocationProbe.Measure(() => calls++, warmups, measurements));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void NullOperationsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => AllocationProbe.Measure(null!));
        Assert.Throws<ArgumentNullException>(() => AllocationProbe.Measure<int>(null!, out _));
    }

    [Fact]
    public void AssertionsReportEverySampleAndEnforceInclusiveAndStrictBoundsOnlyInRelease()
    {
        var calls = 0;
        var result = AllocationProbe.Measure(() => GC.KeepAlive(new byte[++calls * 1024]));
        result.AssertAtMost(result.MinimumBytes);
        result.AssertInRange(result.MinimumBytes, result.MinimumBytes);
        result.AssertLessThan(result.MinimumBytes + 1);
        result.AssertGreaterThan(result.MinimumBytes - 1);
        Action[] failures =
        [
            () => result.AssertAtMost(-123, "allocation context"),
            () => result.AssertLessThan(result.MinimumBytes),
            () => result.AssertInRange(result.MinimumBytes + 1, long.MaxValue),
            () => result.AssertGreaterThan(result.MinimumBytes),
        ];
        foreach (var assertion in failures)
        {
#if DEBUG
            assertion();
            var message = TestContext.Current.TestOutputHelper!.Output;
            Assert.Contains("bounds enforced in Release only", message);
#else
            var message = Assert.Throws<Xunit.Sdk.TrueException>(assertion).Message;
#endif
            Assert.Contains("Allocation bound:", message);
            Assert.Contains(result.ToString(), message);
            Assert.All(result.Samples, sample => Assert.Contains(sample.ToString("N0", CultureInfo.InvariantCulture), message));
        }
#if DEBUG
        var contextual = TestContext.Current.TestOutputHelper!.Output;
#else
        var contextual = Assert.Throws<Xunit.Sdk.TrueException>(failures[0]).Message;
#endif
        Assert.Contains("-123", contextual);
        Assert.Contains("allocation context", contextual);
    }

    [Fact]
    public void NormalizationRetainsSampleOrderAndDoesNotChangeTheOriginal()
    {
        var result = AllocationProbe.Measure(() => { });
        var normalized = result.TransformSamples(bytes => bytes + 123);
        Assert.Equal(result.Samples.Select(bytes => bytes + 123), normalized.Samples);
        Assert.Equal(result.MinimumBytes + 123, normalized.MinimumBytes);
    }

    [Fact]
    public void FreshOpensDisposeEarlierResultsOutsideMeasurementAndLeaveTheLastForTheCaller()
    {
        var opened = new List<DisposableResult>();
        var result = AllocationProbe.MeasureOpen(() =>
        {
            Assert.All(opened, previous => Assert.True(previous.Disposed));
            var next = new DisposableResult();
            opened.Add(next);
            return next;
        }, out var last, firstView: item => item.Views++);
        using (last)
        {
            Assert.Equal(4, opened.Count);
            Assert.Equal(3, result.Samples.Count);
            Assert.Same(opened[^1], last);
            Assert.False(last.Disposed);
            Assert.All(opened, item => Assert.Equal(1, item.Views));
        }
        Assert.True(last.Disposed);
    }

    [Fact]
    public void FailingFirstViewDisposesItsDocumentAndPropagatesTheException()
    {
        var opened = new DisposableResult();
        Assert.Throws<InvalidOperationException>(() => AllocationProbe.MeasureOpen<DisposableResult>(() => opened, out _,
            firstView: _ => throw new InvalidOperationException("view failed")));
        Assert.True(opened.Disposed);
    }

    [Fact]
    public void FailingMeasuredOpenDoesNotDisposeAnEarlierResultTwice()
    {
        var previous = new DisposableResult();
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => AllocationProbe.MeasureOpen(() =>
        {
            if (++calls == 2) throw new InvalidOperationException("open failed");
            return previous;
        }, out _));
        Assert.Equal(1, previous.Disposals);
    }

    private sealed class DisposableResult : IDisposable
    {
        public bool Disposed { get; private set; }
        public int Disposals { get; private set; }
        public int Views { get; set; }
        public void Dispose()
        {
            GC.KeepAlive(new byte[10_000]);
            Disposals++;
            Disposed = true;
        }
    }
}
