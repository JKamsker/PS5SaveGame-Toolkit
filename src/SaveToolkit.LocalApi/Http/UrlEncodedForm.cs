using System.Text;

namespace SaveToolkit.LocalApi.Http;

/// <summary>
/// Reads <c>application/x-www-form-urlencoded</c> like ASP.NET Core's <c>FormPipeReader</c> with default
/// <c>FormOptions</c>: the charset selects the encoding, raw keys over 2 KiB and values over 4 MiB are
/// rejected, and every pair (even an empty one between two <c>&amp;</c>) counts toward the value limit.
/// </summary>
internal static class UrlEncodedForm
{
    public const int KeyLengthLimit = 2 * 1024;
    public const int ValueLengthLimit = 4 * 1024 * 1024;

    public static void Read(MediaType contentType, ReadOnlySpan<byte> body, int valueCountLimit, FormData form)
    {
        var encoding = Charsets.FormEncoding(contentType);
        // UTF-8 and ASCII take the byte decoder; other encodings decode first and unescape the text.
        // Known divergence: for UTF-16/UTF-32 bodies over 4 KiB, ASP.NET's FormReader can drop an empty pair that
        // straddles its first buffer refill; this reader keeps it. Unobservable at the API (uploads need file parts).
        var bytewise = ReferenceEquals(encoding, Encoding.UTF8) || ReferenceEquals(encoding, Encoding.ASCII);
        ReadOnlySpan<byte> equals = bytewise ? "="u8 : encoding.GetBytes("=");
        ReadOnlySpan<byte> and = bytewise ? "&"u8 : encoding.GetBytes("&");
        while (!body.IsEmpty)
        {
            var end = body.IndexOf(and);
            var pair = end >= 0 ? body[..end] : body;
            body = end >= 0 ? body[(end + and.Length)..] : default;

            var split = pair.IndexOf(equals);
            var key = split >= 0 ? pair[..split] : pair;
            var value = split >= 0 ? pair[(split + equals.Length)..] : default;
            if (key.Length > KeyLengthLimit) throw new InvalidDataException($"Form key length limit {KeyLengthLimit} exceeded.");
            if (value.Length > ValueLengthLimit) throw new InvalidDataException($"Form value length limit {ValueLengthLimit} exceeded.");

            form.Values.Add(new(Decode(key, encoding, bytewise), Decode(value, encoding, bytewise)));
            if (form.Values.Count > valueCountLimit)
                throw new InvalidDataException($"Form value count limit {valueCountLimit} exceeded.");
        }
    }

    private static string Decode(ReadOnlySpan<byte> raw, Encoding encoding, bool bytewise)
    {
        if (raw.IsEmpty) return string.Empty;
        if (!bytewise) return Uri.UnescapeDataString(encoding.GetString(raw).Replace('+', ' '));
        var buffer = raw.ToArray();
        return encoding.GetString(buffer, 0, DecodeInPlace(buffer));
    }

    /// <summary>
    /// <c>UrlDecoder.DecodeInPlace(isFormEncoding: true)</c>: '+' is a space, escapes forming valid UTF-8
    /// (no surrogates, overlongs or values above U+10FFFF) are decoded, other escapes stay literal and an
    /// escaped NUL rejects the form.
    /// </summary>
    private static int DecodeInPlace(byte[] buffer)
    {
        int source = 0, destination = 0;
        while (source < buffer.Length)
        {
            var value = buffer[source];
            if (value != '%')
            {
                buffer[destination++] = value == '+' ? (byte)' ' : value;
                source++;
                continue;
            }

            var scan = source;
            if (!TryDecodeSequence(buffer, ref scan, ref destination))
            {
                while (source < scan) buffer[destination++] = buffer[source++];
            }

            source = scan;
        }

        return destination;
    }

    private static bool TryDecodeSequence(byte[] buffer, ref int scan, ref int destination)
    {
        var first = Unescape(buffer, ref scan);
        if (first < 0) return false;
        if (first == 0) throw new InvalidDataException("The form value contains invalid characters.");
        if (first <= 0x7F)
        {
            buffer[destination++] = (byte)first;
            return true;
        }

        var (bits, count, minimum) = (first & 0xE0) == 0xC0 ? (first & 0x1F, 2, 0x80)
            : (first & 0xF0) == 0xE0 ? (first & 0x0F, 3, 0x800)
            : (first & 0xF8) == 0xF0 ? (first & 0x07, 4, 0x10000)
            : (0, 0, 0);
        if (count == 0) return false;

        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)first;
        for (var i = 1; i < count; i++)
        {
            if (scan == buffer.Length) return false;
            var next = scan;
            var continuation = Unescape(buffer, ref next);
            if (continuation < 0 || (continuation & 0xC0) != 0x80) return false;
            bits = (bits << 6) | (continuation & 0x3F);
            var remaining = count - 1 - i;
            if (remaining == 1 && bits is >= 0x360 and <= 0x37F) return false; // a UTF-16 surrogate
            if (remaining == 2 && bits >= 0x110) return false; // above U+10FFFF
            scan = next;
            bytes[i] = (byte)continuation;
        }

        if (bits < minimum) return false; // overlong
        bytes[..count].CopyTo(buffer.AsSpan(destination));
        destination += count;
        return true;
    }

    /// <summary>Consumes the byte at <paramref name="scan"/> and, when it starts a valid escape, its two hex digits.</summary>
    private static int Unescape(byte[] buffer, ref int scan)
    {
        if (buffer[scan++] != '%') return -1;
        if (scan + 1 >= buffer.Length) return -1;
        var high = Hex(buffer[scan]);
        var low = Hex(buffer[scan + 1]);
        if (high < 0 || low < 0) return -1;
        scan += 2;
        return (high << 4) + low;
    }

    private static int Hex(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        _ => -1,
    };
}
