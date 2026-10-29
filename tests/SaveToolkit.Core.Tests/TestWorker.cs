namespace SaveToolkit.Core.Tests;

/// <summary>Runs synchronous blocking fixtures without consuming the shared thread pool.</summary>
internal static class TestWorker
{
    internal static Task Run(Action operation, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(operation, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    internal static Task<T> Run<T>(Func<T> operation, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(operation, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    internal static async Task WaitForSignalAsync(Task signal, Task producer, CancellationToken cancellationToken)
    {
        await Task.WhenAny(signal, producer).WaitAsync(cancellationToken);
        // A producer may legitimately finish immediately after signaling. Prefer the signal in that case.
        if (signal.IsCompleted)
        {
            await signal;
            return;
        }

        await producer;
        Assert.Fail("Fixture producer completed without signaling readiness.");
    }
}
