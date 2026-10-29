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
}
