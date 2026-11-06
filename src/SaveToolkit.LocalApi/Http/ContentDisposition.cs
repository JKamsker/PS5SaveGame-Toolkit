using System.Buffers;
using System.Globalization;
using System.Text;

namespace SaveToolkit.LocalApi.Http;

/// <summary>
/// Content-Disposition parsing for multipart sections and formatting for downloads, matching
/// <c>Microsoft.Net.Http.Headers.ContentDispositionHeaderValue</c>.
/// </summary>
internal sealed class ContentDisposition
{
    private const string AttrChars = "!#$&+-.^_`|~";
    private readonly IReadOnlyList<KeyValuePair<string, string?>> _parameters;

    private ContentDisposition(string type, IReadOnlyList<KeyValuePair<string, string?>> parameters)
    {
        DispositionType = type;
        _parameters = parameters;
    }

    public string DispositionType { get; }

    public string? Name => Decoded("name");

    public string? FileName => Decoded("filename");

    public string? FileNameStar => Raw("filename*") is { } value && TryDecode5987(value, out var decoded) ? decoded : null;

    public bool IsFile => DispositionType == "form-data" && (!string.IsNullOrEmpty(FileName) || !string.IsNullOrEmpty(FileNameStar));

    public bool IsFormValue => DispositionType == "form-data" && string.IsNullOrEmpty(FileName) && string.IsNullOrEmpty(FileNameStar);

    public static ContentDisposition? Parse(string? input)
    {
        if (string.IsNullOrEmpty(input)) return null;
        var index = HttpSyntax.WhitespaceLength(input, 0);
        var typeLength = HttpSyntax.TokenLength(input, index);
        if (typeLength == 0) return null;
        var type = input.Substring(index, typeLength);
        index += typeLength;
        var parameters = MediaType.ParseParameters(input, ref index);
        return parameters is null ? null : new ContentDisposition(type, parameters);
    }

    /// <summary>The <c>attachment</c> header ASP.NET file results send for a download name.</summary>
    public static string Attachment(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "attachment";
        var sanitized = new StringBuilder(fileName.Length);
        foreach (var c in fileName) sanitized.Append(c > 0x7F || char.IsControl(c) ? '_' : c);
        return $"attachment; filename={Quote(sanitized.ToString())}; filename*={Encode5987(fileName)}";
    }

    private string? Raw(string name) =>
        _parameters.Where(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Value).FirstOrDefault();

    private string? Decoded(string name) =>
        Raw(name) is not { } value ? null : TryDecodeMime(value, out var decoded) ? decoded : HttpSyntax.RemoveQuotes(value);

    private static string Quote(string value)
    {
        if (value.Length > 0 && HttpSyntax.TokenLength(value, 0) == value.Length) return value;
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Encode5987(string input)
    {
        var builder = new StringBuilder("UTF-8''");
        var remaining = input.AsSpan();
        Span<byte> utf8 = stackalloc byte[4];
        while (remaining.Length > 0)
        {
            var c = remaining[0];
            if (c < 0x80 && (char.IsAsciiLetterOrDigit(c) || AttrChars.Contains(c, StringComparison.Ordinal)))
            {
                builder.Append(c);
                remaining = remaining[1..];
                continue;
            }

            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done) rune = Rune.ReplacementChar;
            var written = rune.EncodeToUtf8(utf8);
            foreach (var b in utf8[..written]) builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            remaining = remaining[Math.Max(consumed, 1)..];
        }

        return builder.ToString();
    }

    private static bool TryDecodeMime(string input, out string? output)
    {
        output = null;
        // Require quotes, min of "=?e?b??="
        if (input.Length < 10 || input[0] != '"' || input[^1] != '"') return false;
        var parts = input.Split('?');
        if (parts.Length != 5 || parts[0] != "\"=" || parts[4] != "=\"" || !string.Equals(parts[2], "b", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var encoding = Encoding.GetEncoding(parts[1]);
            output = encoding.GetString(Convert.FromBase64String(parts[3]));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return false;
        }
    }

    private static bool TryDecode5987(string input, out string? output)
    {
        output = null;
        var parts = input.Split('\'');
        if (parts.Length != 3) return false;
        try
        {
            var encoding = Encoding.GetEncoding(parts[0]);
            var data = parts[2];
            var decoded = new StringBuilder();
            var pending = new List<byte>();
            for (var index = 0; index < data.Length; index++)
            {
                if (index + 2 < data.Length && data[index] == '%' && Uri.IsHexDigit(data[index + 1]) && Uri.IsHexDigit(data[index + 2]))
                {
                    pending.Add(byte.Parse(data.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += 2;
                    continue;
                }

                if (pending.Count > 0)
                {
                    decoded.Append(encoding.GetString([.. pending]));
                    pending.Clear();
                }

                decoded.Append(data[index]);
            }

            if (pending.Count > 0) decoded.Append(encoding.GetString([.. pending]));
            output = decoded.ToString();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
