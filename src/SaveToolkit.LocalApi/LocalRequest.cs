namespace SaveToolkit.LocalApi;

/// <summary>An HTTP-shaped request dispatched to <see cref="LocalApiHost"/> without a network.</summary>
/// <param name="Method">The HTTP method, e.g. <c>GET</c>.</param>
/// <param name="PathAndQuery">The raw, percent-encoded request target, e.g. <c>/api/workspaces/abc?x=1</c>.</param>
/// <param name="Headers">Request headers (names are case-insensitive; repeats are allowed).</param>
/// <param name="Body">The raw request body (JSON, multipart/form-data, ...).</param>
public sealed record LocalRequest(
    string Method,
    string PathAndQuery,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    ReadOnlyMemory<byte> Body);

/// <summary>The complete response for a <see cref="LocalRequest"/>.</summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Headers">Response headers such as Content-Type, ETag, Content-Disposition and Allow.</param>
/// <param name="Body">The response body; empty for 204 and for aborted requests.</param>
public sealed record LocalResponse(int Status, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body);
