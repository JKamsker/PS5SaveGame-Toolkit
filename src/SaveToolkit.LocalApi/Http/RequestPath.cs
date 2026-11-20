using System.Text;

namespace SaveToolkit.LocalApi.Http;

/// <summary>
/// Decodes a raw request path like ASP.NET's <c>UrlDecoder.DecodeRequestLine</c> (Kestrel and TestServer):
/// percent-escapes forming valid UTF-8 are decoded, other escapes stay literal, and an encoded slash stays
/// <c>%2F</c> so it can never split a route segment.
/// </summary>
internal static class RequestPath
{
    /// <param name="pathAndQuery">An origin-form target (<c>/api/x?y</c>); an absolute URL's scheme and authority are ignored.</param>
    public static (string Path, string Query) Split(string pathAndQuery)
    {
        var target = pathAndQuery;
        var scheme = target.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0 && target.AsSpan(0, scheme).IndexOfAny('/', '?') < 0)
        {
            var pathStart = target.IndexOfAny(['/', '?', '#'], scheme + 3);
            target = pathStart < 0 ? "/" : target[pathStart..];
        }

        var fragment = target.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0) target = target[..fragment];
        var question = target.IndexOf('?', StringComparison.Ordinal);
        var rawPath = question >= 0 ? target[..question] : target;
        var query = question >= 0 ? target[question..] : string.Empty;
        if (rawPath.Length == 0 || rawPath[0] != '/') rawPath = "/" + rawPath;
        return (RemoveDotSegments(Decode(rawPath)), query);
    }

    /// <exception cref="InvalidOperationException">
    /// The path has an escaped NUL (<c>%00</c>). The decoder rejects it before routing: TestServer throws this
    /// same exception to the caller, while Kestrel answers a bare 400 without running the app.
    /// </exception>
    public static string Decode(string raw)
    {
        if (raw.IndexOf('%', StringComparison.Ordinal) < 0) return raw;
        if (raw.Contains("%00", StringComparison.Ordinal)) throw new InvalidOperationException("The path contains null characters.");
        var builder = new StringBuilder(raw.Length);
        var bytes = new List<byte>();
        var index = 0;
        while (index < raw.Length)
        {
            if (TryHex(raw, index, out var value) && value != '/')
            {
                bytes.Clear();
                var start = index;
                while (index < raw.Length && TryHex(raw, index, out var next) && next != '/')
                {
                    bytes.Add(next);
                    index += 3;
                }

                AppendBytes(builder, raw, start, bytes);
                continue;
            }

            builder.Append(raw[index]);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>Decodes valid UTF-8 runs and keeps the original escapes of any invalid byte.</summary>
    private static void AppendBytes(StringBuilder builder, string raw, int start, List<byte> bytes)
    {
        var position = 0;
        while (position < bytes.Count)
        {
            var length = SequenceLength(bytes, position);
            if (length > 0)
            {
                builder.Append(Encoding.UTF8.GetString([.. bytes.GetRange(position, length)]));
                position += length;
            }
            else
            {
                builder.Append(raw, start + (position * 3), 3);
                position++;
            }
        }
    }

    private static int SequenceLength(List<byte> bytes, int position)
    {
        var first = bytes[position];
        var length = first < 0x80 ? 1 : first is >= 0xC2 and <= 0xDF ? 2 : first is >= 0xE0 and <= 0xEF ? 3 : first is >= 0xF0 and <= 0xF4 ? 4 : 0;
        if (length == 0 || position + length > bytes.Count) return 0;
        for (var i = 1; i < length; i++)
            if ((bytes[position + i] & 0xC0) != 0x80) return 0;
        var candidate = bytes.GetRange(position, length).ToArray();
        try
        {
            _ = new UTF8Encoding(false, true).GetString(candidate);
            return length;
        }
        catch (DecoderFallbackException)
        {
            return 0;
        }
    }

    private static bool TryHex(string raw, int index, out byte value)
    {
        value = 0;
        if (index + 2 >= raw.Length || raw[index] != '%') return false;
        var high = HexValue(raw[index + 1]);
        var low = HexValue(raw[index + 2]);
        if (high < 0 || low < 0) return false;
        value = (byte)((high << 4) | low);
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    /// <summary>RFC 3986 dot-segment removal, as Kestrel applies to every request target.</summary>
    private static string RemoveDotSegments(string path)
    {
        if (!path.Contains("/.", StringComparison.Ordinal)) return path;
        var output = new List<string>();
        var segments = path.Split('/');
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            var last = i == segments.Length - 1;
            if (segment == ".")
            {
                if (last) output.Add(string.Empty);
                continue;
            }

            if (segment == "..")
            {
                if (output.Count > 0) output.RemoveAt(output.Count - 1);
                if (last) output.Add(string.Empty);
                continue;
            }

            output.Add(segment);
        }

        return "/" + string.Join('/', output);
    }
}
