using System.Text;

namespace SaveToolkit.LocalApi.Http;

/// <summary>
/// Charset handling of ASP.NET Core's form and JSON readers. Encodings come from
/// <see cref="Encoding.GetEncoding(string)"/> in both hosts: neither registers a code-page provider, so
/// the same built-in names (UTF-8/16/32, ASCII, Latin-1 and their aliases) are known on the server and on
/// browser WebAssembly, and every other charset is unknown in both.
/// </summary>
internal static class Charsets
{
    private const int Utf7CodePage = 65000;

    /// <summary>
    /// <c>MediaTypeHeaderValue.Encoding</c>: the raw <c>charset</c> value (quotes kept, so a quoted name is
    /// unknown), or null when it is missing, valueless or unknown.
    /// </summary>
    public static Encoding? FromMediaType(MediaType? mediaType)
    {
        var charset = mediaType?.Parameter("charset");
        if (string.IsNullOrEmpty(charset)) return null;
        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The form readers' encoding: the declared charset unless unknown or UTF-7, otherwise UTF-8.</summary>
    public static Encoding FormEncoding(MediaType? mediaType) =>
        FromMediaType(mediaType) is { CodePage: not Utf7CodePage } encoding ? encoding : Encoding.UTF8;

    /// <summary>
    /// A multipart text value as <c>MultipartSection.ReadAsStringAsync</c> reads it: a
    /// <c>StreamReader</c> with the section's charset and byte-order-mark detection.
    /// </summary>
    /// <remarks>
    /// The detection sees the whole value. ASP.NET's first 1 KiB read can only be shorter than the first
    /// four bytes when a 4 KiB buffer refill splits them at a partial delimiter, which is not reproduced.
    /// </remarks>
    public static string ReadSectionText(string? contentType, ReadOnlySpan<byte> bytes)
    {
        var encoding = FormEncoding(MediaType.Parse(contentType));
        var preamble = encoding.Preamble;
        if (preamble.Length > 0 && !bytes.IsEmpty)
        {
            // The declared encoding's own preamble is skipped and disables detection; a body that is a
            // shorter prefix of it is decoded as is.
            if (bytes.StartsWith(preamble)) return encoding.GetString(bytes[preamble.Length..]);
            if (bytes.Length < preamble.Length && preamble.StartsWith(bytes)) return encoding.GetString(bytes);
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes[2..]);
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return bytes.Length < 4 || bytes[2] != 0 || bytes[3] != 0
                    ? Encoding.Unicode.GetString(bytes[2..])
                    : Encoding.UTF32.GetString(bytes[4..]);
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes[3..]);
            if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                return new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetString(bytes[4..]);
        }

        return encoding.GetString(bytes);
    }

    /// <summary>
    /// The UTF-8 JSON payload <c>HttpRequest.ReadFromJsonAsync</c> deserializes: the body as is for no charset
    /// or a UTF-8 one, otherwise transcoded from the declared charset. An unknown charset (including a quoted
    /// name) is ASP.NET's <see cref="InvalidOperationException"/>, which both hosts report as a 409.
    /// </summary>
    public static ReadOnlyMemory<byte> JsonUtf8(MediaType mediaType, ReadOnlyMemory<byte> body)
    {
        var charset = mediaType.Parameter("charset");
        // A valueless "charset" means none; an empty "charset=" is looked up (and unknown).
        if (charset is null || charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase)) return body;
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(charset);
        }
#pragma warning disable CA1031 // ASP.NET wraps every failure of the lookup in the same InvalidOperationException.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            throw new InvalidOperationException(
                $"Unable to read the request as JSON because the request content type charset '{charset}' is not a known encoding.", exception);
        }

        // The transcoding stream keeps a decoded byte-order mark; System.Text.Json skips it as a UTF-8 BOM.
        return encoding.CodePage == Encoding.UTF8.CodePage ? body : Encoding.UTF8.GetBytes(encoding.GetString(body.Span));
    }
}
