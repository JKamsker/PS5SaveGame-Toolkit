using SaveToolkit.LocalApi.Protocol;

namespace SaveToolkit.LocalApi.Http;

/// <summary>Per-request state: the decoded request, matched route values and pending response headers.</summary>
internal sealed class LocalHttpContext(LocalRequest request, string path, IQueryValues query, CancellationToken aborted) : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public LocalRequest Request { get; } = request;

    /// <summary>
    /// The method, upper-cased. ASP.NET routing matches methods case-insensitively (and TestServer
    /// canonicalizes known ones), so <c>get</c> and <c>GET</c> behave alike there too; only Kestrel keeps a
    /// lower-case <c>head</c> as a custom method, which routes the same way.
    /// </summary>
    public string Method { get; } = request.Method.ToUpperInvariant();

    /// <summary>The decoded path (an encoded slash stays <c>%2F</c>).</summary>
    public string Path { get; } = path;

    public IQueryValues Query { get; } = query;

    public ReadOnlyMemory<byte> Body => Request.Body;

    public CancellationToken Aborted { get; } = aborted;

    public Dictionary<string, string> RouteValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<KeyValuePair<string, string>> ResponseHeaders { get; } = [];

    /// <summary>Request-scoped values, such as the workspace lease shared by route binders.</summary>
    public Dictionary<object, object> Items { get; } = [];

    public string? ContentType => Header("Content-Type");

    public string? Header(string name) => HeaderValues(name).FirstOrDefault();

    public IEnumerable<string> HeaderValues(string name) =>
        Request.Headers.Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value);

    public void SetResponseHeader(string name, string value)
    {
        ResponseHeaders.RemoveAll(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase));
        ResponseHeaders.Add(new(name, value));
    }

    /// <summary>Disposed after the response is complete, like <c>HttpResponse.RegisterForDispose</c>.</summary>
    public void RegisterForDispose(IDisposable disposable) => _disposables.Add(disposable);

    public void Dispose()
    {
        for (var i = _disposables.Count - 1; i >= 0; i--) _disposables[i].Dispose();
        _disposables.Clear();
    }
}
