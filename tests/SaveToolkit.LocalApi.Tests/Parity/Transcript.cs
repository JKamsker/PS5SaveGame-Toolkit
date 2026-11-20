using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaveToolkit.LocalApi.Tests.Parity;

/// <summary>One response as the scenario sees it.</summary>
internal sealed record Exchange(int Status, string? ContentType, byte[] Body)
{
    public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();

    public string Text => Encoding.UTF8.GetString(Body);
}

/// <summary>
/// Records normalized exchanges for one host. Volatile values (ids registered with <see cref="Alias"/>,
/// timestamps, ZIP entry metadata) are replaced so the two hosts' transcripts compare exactly.
/// </summary>
internal sealed partial class Transcript(HttpClient client)
{
    private static readonly string[] ComparedHeaders =
    [
        "Content-Type", "ETag", "Content-Disposition", "Allow", "Retry-After", "Cache-Control",
        "X-Content-Type-Options", "Content-Security-Policy", "Pragma", "Expires",
    ];

    private readonly List<KeyValuePair<string, string>> _aliases = [];
    private readonly List<string> _raw = [];

    /// <summary>Normalized entries; aliases registered after an exchange still apply to it.</summary>
    public IReadOnlyList<string> Entries => [.. _raw.Select(Normalize)];

    public HttpClient Client { get; } = client;

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Replaces <paramref name="value"/> with <paramref name="placeholder"/> in every later entry.</summary>
    public void Alias(string? value, string placeholder)
    {
        // Short deterministic ids (d0, f0) are identical on both hosts and would corrupt placeholders.
        if (value is not { Length: >= 8 } || _aliases.Any(a => a.Key == value)) return;
        _aliases.Add(new(value, placeholder));
        _aliases.Sort((left, right) => right.Key.Length.CompareTo(left.Key.Length));
    }

    public Task<Exchange> GetAsync(string url) => SendAsync(HttpMethod.Get, url);

    public Task<Exchange> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url);

    public Task<Exchange> PostJsonAsync(string url, string json, string? ifMatch = null, string contentType = "application/json") =>
        SendAsync(HttpMethod.Post, url, new StringContent(json, Encoding.UTF8, MediaTypeHeaderValue.Parse(contentType)), ifMatch);

    public Task<Exchange> SendAsync(HttpMethod method, string url, HttpContent? content = null, string? ifMatch = null,
        params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative)) { Content = content };
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        return SendAsync(request);
    }

    public async Task<Exchange> SendAsync(HttpRequestMessage request)
    {
        using (request)
        using (var response = await Client.SendAsync(request, Ct))
        {
            var body = await response.Content.ReadAsByteArrayAsync(Ct);
            var builder = new StringBuilder();
            builder.Append(request.Method.Method).Append(' ').Append(request.RequestUri!.OriginalString).Append('\n');
            builder.Append("status: ").Append((int)response.StatusCode).Append('\n');
            foreach (var name in ComparedHeaders)
            {
                var values = response.Headers.TryGetValues(name, out var found) ? found
                    : response.Content.Headers.TryGetValues(name, out var content) ? content : null;
                if (values is not null) builder.Append(name).Append(": ").Append(string.Join(", ", values)).Append('\n');
            }

            builder.Append(DescribeBody(response.Content.Headers.ContentType?.MediaType, body));
            _raw.Add(builder.ToString());
            if (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.Ordinal) == true) AliasIds(body);
            return new Exchange((int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), body);
        }
    }

    /// <summary>Registers generated identifiers (workspace, document, plan, preset and token ids) in order of appearance.</summary>
    private void AliasIds(byte[] body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            Visit(json.RootElement);
        }
        catch (JsonException)
        {
            // Not JSON after all; nothing to register.
        }

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray()) Visit(item);
                return;
            }

            if (element.ValueKind != JsonValueKind.Object) return;
            foreach (var property in element.EnumerateObject())
            {
                if (GeneratedIds.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String
                    && property.Value.GetString() is { Length: >= 12 } id)
                    Alias(id, $"<{property.Name}{_aliases.Count}>");
                Visit(property.Value);
            }
        }
    }

    private static readonly HashSet<string> GeneratedIds = new(StringComparer.Ordinal)
    {
        "id", "planId", "token", "proposalToken", "documentId", "openDocumentId", "presetId",
    };

    private static string DescribeBody(string? mediaType, byte[] body)
    {
        if (body.Length == 0) return "body: <empty>";
        if (mediaType == "application/zip")
        {
            using var zip = new ZipArchive(new MemoryStream(body), ZipArchiveMode.Read);
            return "zip: " + string.Join("; ", zip.Entries.Select(e =>
            {
                using var stream = e.Open();
                return $"{e.FullName}={Convert.ToHexString(SHA256.HashData(stream))}";
            }));
        }

        return mediaType is { } type && (type.Contains("json", StringComparison.Ordinal) || type.StartsWith("text/", StringComparison.Ordinal))
            ? "body: " + Encoding.UTF8.GetString(body)
            : $"bytes: {body.Length} {Convert.ToHexString(SHA256.HashData(body))}";
    }

    private string Normalize(string text)
    {
        foreach (var (value, placeholder) in _aliases) text = text.Replace(value, placeholder, StringComparison.Ordinal);
        return Timestamp().Replace(text, "<time>");
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})")]
    private static partial Regex Timestamp();
}
