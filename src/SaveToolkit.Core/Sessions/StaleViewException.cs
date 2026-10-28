namespace SaveToolkit.Core.Sessions;

/// <summary>
/// A client used an id from a view that a structural change has replaced. The client should refetch
/// the document view and retry. The API maps this to HTTP 409 with <c>code = "stale"</c>.
/// </summary>
public sealed class StaleViewException : InvalidOperationException
{
    public const string Code = "stale";

    public StaleViewException()
        : base(DefaultMessage)
    {
    }

    public StaleViewException(string message)
        : base(message)
    {
    }

    public StaleViewException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal const string DefaultMessage = "The document structure changed; this view is out of date. Refresh and try again.";
}
