using System.Net;
using SaveToolkit.LocalApi;

namespace SaveToolkit.LocalApi.Tests.Parity;

/// <summary>Adapts <see cref="HttpClient"/> requests to <see cref="LocalApiHost.SendAsync"/>, as the browser worker does.</summary>
internal sealed class LocalApiHandler(LocalApiHost host) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("A request URI is required.");
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in request.Headers)
            foreach (var value in header.Value) headers.Add(new(header.Key, value));
        byte[] body = [];
        if (request.Content is { } content)
        {
            body = await content.ReadAsByteArrayAsync(cancellationToken);
            foreach (var header in content.Headers)
                foreach (var value in header.Value) headers.Add(new(header.Key, value));
        }

        if (!headers.Any(h => string.Equals(h.Key, "Host", StringComparison.OrdinalIgnoreCase)))
            headers.Add(new("Host", uri.Authority));

        var response = await host.SendAsync(new LocalRequest(request.Method.Method, uri.PathAndQuery, headers, body), cancellationToken);
        var message = new HttpResponseMessage((HttpStatusCode)response.Status)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(response.Body),
        };
        foreach (var (name, value) in response.Headers)
        {
            if (!message.Headers.TryAddWithoutValidation(name, value)) message.Content.Headers.TryAddWithoutValidation(name, value);
        }

        return message;
    }
}
