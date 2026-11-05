namespace SaveToolkit.Browser;

/// <summary>Completed response frames waiting for the worker to collect them (single-threaded runtime).</summary>
internal static class PendingResponses
{
    private static readonly Dictionary<int, byte[]> Frames = [];
    private static int _next;

    public static int Add(byte[] frame)
    {
        var handle = unchecked(++_next);
        Frames[handle] = frame;
        return handle;
    }

    public static byte[] Take(int handle)
    {
        if (!Frames.Remove(handle, out var frame)) throw new KeyNotFoundException($"Unknown response handle {handle}.");
        return frame;
    }
}
