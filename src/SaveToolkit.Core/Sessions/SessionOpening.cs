namespace SaveToolkit.Core.Sessions;

/// <summary>Releases unpublished candidates while retaining the original open failure.</summary>
internal static class SessionOpening
{
    public static void DisposeFailed(IDisposable candidate, Exception failure)
    {
        try { candidate.Dispose(); }
        catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
        {
            throw new AggregateException("Document opening and cleanup failed.", failure, cleanup);
        }
    }
}
