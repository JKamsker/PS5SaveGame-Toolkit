namespace SaveToolkit.LocalApi.Protocol;

/// <summary>
/// A transport-shape rejection with an explicit HTTP status (default 400). Both hosts map it like
/// ASP.NET Core's <c>BadHttpRequestException</c>: the status and message become the problem body.
/// </summary>
internal sealed class ApiStatusException : Exception
{
    public ApiStatusException() : this("Bad Request") { }

    public ApiStatusException(string message) : this(message, 400) { }

    public ApiStatusException(string message, Exception innerException) : base(message, innerException) => StatusCode = 400;

    public ApiStatusException(string message, int statusCode) : base(message) => StatusCode = statusCode;

    public ApiStatusException(string message, int statusCode, Exception? innerException) : base(message, innerException)
        => StatusCode = statusCode;

    public int StatusCode { get; }
}
