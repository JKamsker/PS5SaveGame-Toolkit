namespace SaveToolkit.Core.Sessions;

/// <summary>Retains a removed workspace until the last active operation completes.</summary>
public sealed class WorkspaceLease : IDisposable
{
    private Action? _release;

    internal WorkspaceLease(Workspace workspace, Action release)
    {
        Value = workspace;
        _release = release;
    }

    public Workspace Value { get; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
