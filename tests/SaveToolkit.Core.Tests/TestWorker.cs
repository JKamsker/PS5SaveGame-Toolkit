namespace SaveToolkit.Core.Tests;

/// <summary>Runs synchronous blocking fixtures without consuming the shared thread pool.</summary>
internal static class TestWorker
{
    internal static Task Run(Action operation, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(operation, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    internal static Task<T> Run<T>(Func<T> operation, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(operation, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
