namespace SaveToolkit.Core.Tests;

public sealed class TestWorkerTests
{
    [Fact]
    public async Task BlockingOperationsRunOutsideTheThreadPoolAndReturnTheirResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var actionUsedPool = true;
        await TestWorker.Run(() => { actionUsedPool = Thread.CurrentThread.IsThreadPoolThread; }, ct);
        var result = await TestWorker.Run(() => (UsesThreadPool: Thread.CurrentThread.IsThreadPoolThread, Value: 42), ct);

        Assert.False(actionUsedPool);
        Assert.False(result.UsesThreadPool);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task WorkerExceptionsReachTheAwaitingTest()
    {
        var expected = new InvalidOperationException("Fixture operation failed.");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => TestWorker.Run(
            (Action)(() => throw expected), TestContext.Current.CancellationToken));

        Assert.Same(expected, failure);
    }

    [Fact]
    public async Task CancellationBeforeStartingDoesNotInvokeTheOperation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var invoked = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestWorker.Run(
            () => { invoked = true; }, cancellation.Token));

        Assert.False(invoked);
    }

    [Fact]
    public async Task ReadinessCompletesWhileTheProducerIsStillRunning()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = TestWorker.WaitForSignalAsync(signal.Task, producer.Task, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);

        signal.SetResult();
        await waiting;

        Assert.False(producer.Task.IsCompleted);
    }

    [Fact]
    public async Task ProducerFailureBeforeReadinessReachesTheAwaitingTest()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("Producer failed before its callback.");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => TestWorker.WaitForSignalAsync(
            signal.Task, Task.FromException(expected), TestContext.Current.CancellationToken));

        Assert.Same(expected, failure);
    }

    [Fact]
    public async Task ProducerCancellationBeforeReadinessReachesTheAwaitingTest()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestWorker.WaitForSignalAsync(
            signal.Task, Task.FromCanceled(cancellation.Token), TestContext.Current.CancellationToken));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task SuccessfulProducerWithoutReadinessFailsExplicitly()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => TestWorker.WaitForSignalAsync(
            signal.Task, Task.CompletedTask, TestContext.Current.CancellationToken));

        Assert.Contains("completed without signaling readiness", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedSignalTakesPrecedenceOverCompletedProducer()
    {
        var expected = new InvalidOperationException("Producer failed after signaling.");
        var producer = Task.FromException(expected);

        await TestWorker.WaitForSignalAsync(Task.CompletedTask, producer, TestContext.Current.CancellationToken);

        // The fixture's normal join remains responsible for errors after readiness.
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => producer));
    }

    [Fact]
    public async Task ExternalCancellationStopsAnUnsignaledWait()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var waiting = TestWorker.WaitForSignalAsync(signal.Task, producer.Task, cancellation.Token);

        await cancellation.CancelAsync();

        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
    }
}
