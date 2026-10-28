namespace SaveToolkit.Core.Sessions;

/// <summary>Completes independent cleanup operations before reporting plugin disposal failures.</summary>
internal static class SessionCleanup
{
    public static void RunAll<T>(IEnumerable<T> items, Action<T> cleanup)
    {
        List<Exception> failures = [];
        foreach (var item in items)
        {
            try { cleanup(item); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { failures.Add(ex); }
        }

        if (failures.Count > 0) throw new AggregateException("Session cleanup failed.", failures);
    }
}
