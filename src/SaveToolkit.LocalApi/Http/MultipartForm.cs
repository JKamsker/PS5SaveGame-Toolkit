using System.Text;

namespace SaveToolkit.LocalApi.Http;

/// <summary>One uploaded file part; <see cref="Content"/> is a slice of the request body.</summary>
internal sealed record FormFile(string Name, string FileName, ReadOnlyMemory<byte> Content)
{
    public long Length => Content.Length;
}

/// <summary>A parsed form body (text values and files in request order).</summary>
internal sealed class FormData
{
    public List<KeyValuePair<string, string>> Values { get; } = [];

    public List<FormFile> Files { get; } = [];

    public string? First(string key) =>
        Values.Where(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)).Select(v => v.Value).FirstOrDefault();
}

/// <summary>
/// Reads <c>multipart/form-data</c> with ASP.NET Core <c>FormFeature</c>/<c>MultipartReader</c> semantics and
/// default <c>FormOptions</c> limits. Malformed envelopes throw <see cref="InvalidDataException"/> or
/// <see cref="IOException"/>, which hosts report as a 400 upload error.
/// </summary>
internal static class MultipartForm
{
    private const int BoundaryLengthLimit = 70;
    private const int HeadersCountLimit = 16;
    private const int HeadersLengthLimit = 16 * 1024;
    private const int ValueLengthLimit = 4 * 1024 * 1024;
    private const int KeyLengthLimit = 2048;

    public static FormData Read(MediaType contentType, ReadOnlyMemory<byte> body, long bodyLengthLimit, int valueCountLimit,
        CancellationToken cancellationToken)
    {
        var form = new FormData();
        if (!contentType.IsMultipartForm)
        {
            ReadUrlEncoded(body.Span, form);
            return form;
        }

        var boundary = HttpSyntax.RemoveQuotes(contentType.Parameter("boundary") ?? string.Empty);
        if (string.IsNullOrWhiteSpace(boundary)) throw new InvalidDataException("Missing content-type boundary.");
        if (boundary.Length > BoundaryLengthLimit)
            throw new InvalidDataException($"Multipart boundary length limit {BoundaryLengthLimit} exceeded.");

        var delimiter = Encoding.UTF8.GetBytes("--" + boundary);
        var span = body.Span;
        var position = FirstBoundary(span, delimiter);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            position += delimiter.Length;
            if (span.Length - position >= 2 && span[position] == '-' && span[position + 1] == '-') return form;
            position = SkipBoundaryLine(span, position);
            var headers = ReadHeaders(span, ref position);
            var end = IndexOf(span, position, [(byte)'\r', (byte)'\n', .. delimiter]);
            if (end < 0) throw new IOException("Unexpected end of Stream, the content may have already been read by another component.");
            if (end - position > bodyLengthLimit) throw new InvalidDataException($"Multipart body length limit {bodyLengthLimit} exceeded.");
            AddSection(form, headers, body[position..end], valueCountLimit);
            position = end + 2;
        }
    }

    private static int FirstBoundary(ReadOnlySpan<byte> span, byte[] delimiter)
    {
        // The first boundary may start the body; later ones always follow CRLF.
        if (span.StartsWith(delimiter)) return 0;
        var index = IndexOf(span, 0, [(byte)'\r', (byte)'\n', .. delimiter]);
        if (index < 0) throw new IOException("Unexpected end of Stream, the content may have already been read by another component.");
        return index + 2;
    }

    private static int SkipBoundaryLine(ReadOnlySpan<byte> span, int position)
    {
        while (position < span.Length && span[position] is (byte)' ' or (byte)'\t') position++;
        if (span.Length - position < 2) throw new IOException("Unexpected end of Stream, the content may have already been read by another component.");
        if (span[position] != '\r' || span[position + 1] != '\n') throw new InvalidDataException("Invalid multipart boundary line.");
        return position + 2;
    }

    private static List<KeyValuePair<string, string>> ReadHeaders(ReadOnlySpan<byte> span, ref int position)
    {
        var headers = new List<KeyValuePair<string, string>>();
        var total = 0;
        while (true)
        {
            var lineEnd = IndexOf(span, position, "\r\n"u8);
            if (lineEnd < 0) throw new IOException("Unexpected end of Stream, the content may have already been read by another component.");
            var line = Encoding.UTF8.GetString(span[position..lineEnd]);
            position = lineEnd + 2;
            if (line.Length == 0) return headers;
            total += line.Length;
            if (total > HeadersLengthLimit) throw new InvalidDataException($"Line length limit {HeadersLengthLimit} exceeded.");
            if (headers.Count >= HeadersCountLimit) throw new InvalidDataException($"Multipart headers count limit {HeadersCountLimit} exceeded.");
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0) throw new InvalidDataException($"Invalid header line: {line}");
            headers.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
    }

    private static void AddSection(FormData form, List<KeyValuePair<string, string>> headers, ReadOnlyMemory<byte> content, int valueCountLimit)
    {
        var raw = headers.Where(h => string.Equals(h.Key, "Content-Disposition", StringComparison.OrdinalIgnoreCase))
            .Select(h => h.Value).FirstOrDefault();
        var disposition = ContentDisposition.Parse(raw)
            ?? throw new InvalidDataException("Form section has invalid Content-Disposition value: " + raw);
        if (disposition.IsFile)
        {
            var name = HttpSyntax.RemoveQuotes(disposition.Name ?? string.Empty);
            var fileName = HttpSyntax.RemoveQuotes(disposition.FileNameStar ?? disposition.FileName ?? string.Empty);
            form.Files.Add(new FormFile(name, fileName, content));
        }
        else if (disposition.IsFormValue)
        {
            var key = HttpSyntax.RemoveQuotes(disposition.Name ?? string.Empty);
            if (key.Length > KeyLengthLimit) throw new InvalidDataException($"Form key length limit {KeyLengthLimit} exceeded.");
            if (content.Length > ValueLengthLimit) throw new InvalidDataException($"Multipart body length limit {ValueLengthLimit} exceeded.");
            form.Values.Add(new(key, DecodeText(content.Span)));
        }

        if (form.Values.Count + form.Files.Count > valueCountLimit)
            throw new InvalidDataException($"Form value count limit {valueCountLimit} exceeded.");
    }

    private static string DecodeText(ReadOnlySpan<byte> bytes) =>
        Encoding.UTF8.GetString(bytes.StartsWith(Encoding.UTF8.Preamble) ? bytes[Encoding.UTF8.Preamble.Length..] : bytes);

    private static void ReadUrlEncoded(ReadOnlySpan<byte> body, FormData form)
    {
        var parsed = QueryValues.Parse(Encoding.UTF8.GetString(body));
        foreach (var (key, values) in parsed.Entries)
            foreach (var value in values) form.Values.Add(new(key, value));
    }

    private static int IndexOf(ReadOnlySpan<byte> span, int start, ReadOnlySpan<byte> value)
    {
        var index = span[start..].IndexOf(value);
        return index < 0 ? -1 : start + index;
    }
}
