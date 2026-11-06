using System.Text;

namespace SaveToolkit.LocalApi.Http;

/// <summary>The headers ASP.NET's <c>FormFeature</c> reads from one multipart section (repeated values joined).</summary>
internal sealed record MultipartSection(string? ContentDisposition, string? ContentType);

/// <summary>
/// ASP.NET Core's <c>MultipartReader</c>/<c>MultipartReaderStream</c> over an in-memory body. The boundary
/// line keeps ASP.NET's lenient rules: anything after the delimiter (up to a 100-byte line, trimmed) only
/// decides whether it is the final <c>--</c> boundary, a preamble may end without CRLF, and a section body
/// may end at the end of the body after its delimiter. Failures throw <see cref="InvalidDataException"/> or
/// <see cref="IOException"/> with ASP.NET's messages.
/// </summary>
internal sealed class MultipartReader
{
    public const int HeadersCountLimit = 16;
    public const int HeadersLengthLimit = 16 * 1024;

    /// <summary>Read sizes of <c>DrainAsync</c> (files, skipped sections) and of <c>ReadAsStringAsync</c>'s StreamReader.</summary>
    public const int DrainReadSize = 4 * 1024;
    public const int TextReadSize = 1024;

    private const string UnexpectedEnd = "Unexpected end of Stream, the content may have already been read by another component. ";

    private readonly BufferedBody _body;
    private readonly byte[] _delimiter;
    private readonly int _finalBoundaryLength;
    private readonly long _bodyLengthLimit;
    private bool _started;
    private bool _expectLeadingCrlf;
    private bool _sectionPending;
    private bool _finalBoundaryFound;

    public MultipartReader(ReadOnlyMemory<byte> body, string boundary, long bodyLengthLimit)
    {
        _body = new BufferedBody(body);
        _delimiter = Encoding.UTF8.GetBytes("\r\n--" + boundary);
        // MultipartBoundary computes this once, before the first boundary ("--b" plus a final "--"),
        // so later sections may end at the end of the body directly after "\r\n--b".
        _finalBoundaryLength = _delimiter.Length;
        _bodyLengthLimit = bodyLengthLimit;
    }

    /// <summary>Skips the preamble or the unread rest of the previous section; null after the final boundary.</summary>
    public MultipartSection? ReadNextSection(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            _started = true;
            ReadToBoundary(DrainReadSize, HeadersLengthLimit, cancellationToken);
        }
        else if (_sectionPending)
        {
            ReadSectionBody(DrainReadSize, cancellationToken);
        }

        if (_finalBoundaryFound)
        {
            // The epilogue is drained with the headers length limit.
            if (_body.Remaining > HeadersLengthLimit)
                throw new InvalidDataException($"The stream exceeded the data limit {HeadersLengthLimit}.");
            return null;
        }

        var section = ReadHeaders();
        _expectLeadingCrlf = true;
        _sectionPending = true;
        return section;
    }

    /// <summary>Reads the current section's content (a slice of the body) in reads of <paramref name="readSize"/> bytes.</summary>
    public ReadOnlyMemory<byte> ReadSectionBody(int readSize, CancellationToken cancellationToken)
    {
        _sectionPending = false;
        return ReadToBoundary(readSize, _bodyLengthLimit, cancellationToken);
    }

    private ReadOnlyMemory<byte> ReadToBoundary(int readSize, long lengthLimit, CancellationToken cancellationToken)
    {
        var delimiter = _delimiter.AsSpan(_expectLeadingCrlf ? 0 : 2);
        var start = _body.Position;
        long observed = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_body.EnsureBuffered(_finalBoundaryLength)) throw new IOException(UnexpectedEnd);
            var buffered = _body.Buffered;
            var index = buffered.IndexOf(delimiter);
            var available = index >= 0 ? index : PartialMatchOffset(buffered, delimiter);
            if (available == 0) break;

            var read = Math.Min(readSize, available);
            _body.Consume(read);
            observed += read;
            if (observed > lengthLimit)
            {
                throw new InvalidDataException(_expectLeadingCrlf
                    ? $"Multipart body length limit {lengthLimit} exceeded."
                    : $"Multipart header length limit {lengthLimit} exceeded. Too much data before the first boundary.");
            }
        }

        var content = _body.Slice(start, _body.Position - start);
        _body.Consume(delimiter.Length);
        // "The boundary may be followed by zero or more characters of linear whitespace"; ASP.NET accepts
        // any other remainder as a non-final boundary.
        _finalBoundaryFound = _body.ReadLine(100).Trim() == "--";
        return content;
    }

    /// <summary>Where a trailing prefix of the delimiter starts (<c>SubMatch</c>), else the buffered length.</summary>
    private static int PartialMatchOffset(ReadOnlySpan<byte> buffered, ReadOnlySpan<byte> delimiter)
    {
        for (var offset = Math.Max(0, buffered.Length - delimiter.Length); offset < buffered.Length; offset++)
        {
            if (delimiter.StartsWith(buffered[offset..])) return offset;
        }

        return buffered.Length;
    }

    private MultipartSection ReadHeaders()
    {
        var total = 0;
        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var line = _body.ReadLine(HeadersLengthLimit);
        while (line.Length > 0)
        {
            if (HeadersLengthLimit - total < line.Length)
                throw new InvalidDataException($"Multipart headers length limit {HeadersLengthLimit} exceeded.");
            total += line.Length;
            // The name is neither trimmed nor validated: "Name :" is a different header and ":x" is invalid.
            var split = line.IndexOf(':', StringComparison.Ordinal);
            if (split <= 0) throw new InvalidDataException($"Invalid header line: {line}");
            var name = line[..split];
            if (!headers.TryGetValue(name, out var values)) headers[name] = values = [];
            values.Add(line[(split + 1)..].Trim());
            if (headers.Count > HeadersCountLimit)
                throw new InvalidDataException($"Multipart headers count limit {HeadersCountLimit} exceeded.");
            line = _body.ReadLine(HeadersLengthLimit - total);
        }

        return new MultipartSection(Joined(headers, "Content-Disposition"), Joined(headers, "Content-Type"));
    }

    /// <summary><c>StringValues.ToString()</c>: non-empty values joined with a comma.</summary>
    private static string? Joined(Dictionary<string, List<string>> headers, string name) =>
        headers.TryGetValue(name, out var values) ? string.Join(',', values.Where(v => v.Length > 0)) : null;
}

/// <summary>
/// ASP.NET Core's <c>BufferedReadStream</c> (4 KiB) over a body whose reads return every requested byte.
/// The window only refills when a caller needs more than it holds, which decides where a delimiter can be
/// seen and how long a line may be, so the refill points are reproduced.
/// </summary>
internal sealed class BufferedBody(ReadOnlyMemory<byte> body)
{
    private const int BufferSize = 4 * 1024;
    private int _count;

    public int Position { get; private set; }

    public int Remaining => body.Length - Position;

    public ReadOnlySpan<byte> Buffered => body.Span.Slice(Position, _count);

    public ReadOnlyMemory<byte> Slice(int start, int length) => body.Slice(start, length);

    public bool EnsureBuffered(int minCount)
    {
        if (_count < minCount) _count = Math.Min(BufferSize, Remaining);
        return _count >= minCount;
    }

    public void Consume(int count)
    {
        Position += count;
        _count -= count;
    }

    /// <summary>
    /// Reads to CRLF or the end of the body. Like ASP.NET, the limit applies to the bytes taken from one
    /// buffer fill (CR included), not to the whole line.
    /// </summary>
    public string ReadLine(int lengthLimit)
    {
        var start = Position;
        bool foundCr = false, foundCrLf = false;
        while (!foundCrLf && EnsureBuffered(1))
        {
            var chunk = Buffered;
            // The LF that ends the line (its CR may have ended the previous fill), or -1.
            var lf = foundCr && chunk[0] == '\n' ? 0 : chunk.IndexOf("\r\n"u8) is >= 0 and var crlf ? crlf + 1 : -1;
            // Every byte before that LF counts toward the limit.
            if ((lf >= 0 ? lf : chunk.Length) > lengthLimit) throw new InvalidDataException($"Line length limit {lengthLimit} exceeded.");
            foundCrLf = lf >= 0;
            foundCr = !foundCrLf && chunk[^1] == '\r';
            Consume(foundCrLf ? lf + 1 : chunk.Length);
        }

        var line = body.Span[start..Position];
        return Encoding.UTF8.GetString(foundCrLf ? line[..^2] : line);
    }
}
