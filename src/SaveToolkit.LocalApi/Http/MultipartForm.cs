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
/// Reads a form body like ASP.NET Core's <c>FormFeature</c> with the Api's <c>FormOptions</c>: url-encoded
/// bodies through <see cref="UrlEncodedForm"/>, multipart bodies through <see cref="MultipartReader"/> with
/// default header and boundary limits. Malformed envelopes throw <see cref="InvalidDataException"/> or
/// <see cref="IOException"/>, which hosts report as a 400 upload error.
/// </summary>
internal static class MultipartForm
{
    private const int BoundaryLengthLimit = 128;

    /// <param name="bodyLengthLimit"><c>MultipartBodyLengthLimit</c>: the largest section.</param>
    /// <param name="valueCountLimit"><c>ValueCountLimit</c>: url-encoded pairs or multipart sections.</param>
    public static FormData Read(MediaType contentType, ReadOnlyMemory<byte> body, long bodyLengthLimit, int valueCountLimit,
        CancellationToken cancellationToken)
    {
        var form = new FormData();
        // FormFeature answers a Content-Length: 0 request with an empty form before looking at the body.
        if (body.IsEmpty) return form;
        if (!contentType.IsMultipartForm)
        {
            UrlEncodedForm.Read(contentType, body.Span, valueCountLimit, form);
            return form;
        }

        // FormFeature and MultipartReader each remove one pair of quotes; only an empty boundary is missing.
        var boundary = HttpSyntax.RemoveQuotes(HttpSyntax.RemoveQuotes(contentType.Parameter("boundary") ?? string.Empty));
        if (boundary.Length == 0) throw new InvalidDataException("Missing content-type boundary.");
        if (boundary.Length > BoundaryLengthLimit)
            throw new InvalidDataException($"Multipart boundary length limit {BoundaryLengthLimit} exceeded.");

        var reader = new MultipartReader(body, boundary, bodyLengthLimit);
        var sections = 0;
        while (reader.ReadNextSection(cancellationToken) is { } section)
        {
            if (++sections > valueCountLimit) throw new InvalidDataException($"Form value count limit {valueCountLimit} exceeded.");
            var disposition = ContentDisposition.Parse(section.ContentDisposition)
                ?? throw new InvalidDataException("Form section has invalid Content-Disposition value: " + section.ContentDisposition);
            if (disposition.IsFile)
            {
                var name = HttpSyntax.RemoveQuotes(disposition.Name ?? string.Empty);
                var fileName = HttpSyntax.RemoveQuotes(disposition.FileNameStar ?? disposition.FileName ?? string.Empty);
                form.Files.Add(new FormFile(name, fileName, reader.ReadSectionBody(MultipartReader.DrainReadSize, cancellationToken)));
            }
            else if (disposition.IsFormValue)
            {
                // Multipart keys and values have no length limits of their own (the section limits apply).
                var key = HttpSyntax.RemoveQuotes(disposition.Name ?? string.Empty);
                var content = reader.ReadSectionBody(MultipartReader.TextReadSize, cancellationToken);
                form.Values.Add(new(key, Charsets.ReadSectionText(section.ContentType, content.Span)));
            }
        }

        return form;
    }
}
