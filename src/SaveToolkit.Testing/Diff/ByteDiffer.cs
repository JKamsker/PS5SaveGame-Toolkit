namespace SaveToolkit.Testing.Diff;

/// <summary>
/// Streaming byte comparison with bounded memory: two fixed buffers for the scan, at most
/// <see cref="ByteDiffOptions.MaxRanges"/> recorded ranges, and small hex windows read in a second pass.
/// </summary>
public static class ByteDiffer
{
    private const int BufferSize = 64 * 1024;

    public static ByteDiffResult Compare(byte[] before, byte[] after, IReadOnlyList<ByteInterval>? ignored = null, ByteDiffOptions? options = null) =>
        Compare(() => new MemoryStream(before, writable: false), () => new MemoryStream(after, writable: false), ignored, options);

    /// <param name="openBefore">Opens the before content; called twice (scan, then hex previews).</param>
    /// <param name="openAfter">Opens the after content; called twice.</param>
    /// <param name="ignored">Sorted, non-overlapping masked ranges (see <see cref="FileChurn.ByteIntervals"/>).</param>
    public static ByteDiffResult Compare(Func<Stream> openBefore, Func<Stream> openAfter, IReadOnlyList<ByteInterval>? ignored = null, ByteDiffOptions? options = null)
    {
        options ??= new ByteDiffOptions();
        var scan = new Scanner(options, ignored ?? []);
        using (var before = openBefore())
        using (var after = openAfter())
        {
            scan.Run(before, after);
        }

        var ranges = scan.Ranges;
        if (ranges.Count > 0)
        {
            ranges = WithPreviews(ranges, openBefore, openAfter, options);
        }

        return new ByteDiffResult
        {
            BeforeLength = scan.BeforeLength,
            AfterLength = scan.AfterLength,
            ChangedBytes = scan.ChangedBytes,
            IgnoredBytes = scan.IgnoredBytes,
            RangeCount = scan.RangeCount,
            Ranges = ranges,
        };
    }

    private static List<ByteChange> WithPreviews(IReadOnlyList<ByteChange> ranges, Func<Stream> openBefore, Func<Stream> openAfter, ByteDiffOptions options)
    {
        using var before = openBefore();
        using var after = openAfter();
        if (!before.CanSeek || !after.CanSeek)
        {
            return [.. ranges];
        }

        var result = new List<ByteChange>(ranges.Count);
        foreach (var range in ranges)
        {
            var start = Math.Max(0, range.Offset - options.ContextBytes);
            var count = (int)(range.Offset - start + Math.Min(range.Length, options.MaxPreviewBytes) + options.ContextBytes);
            result.Add(range with
            {
                WindowStart = start,
                BeforeHex = Convert.ToHexStringLower(ReadWindow(before, start, count)),
                AfterHex = Convert.ToHexStringLower(ReadWindow(after, start, count)),
            });
        }

        return result;
    }

    private static byte[] ReadWindow(Stream stream, long start, int count)
    {
        if (start >= stream.Length)
        {
            return [];
        }

        stream.Position = start;
        var buffer = new byte[(int)Math.Min(count, stream.Length - start)];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private sealed class Scanner(ByteDiffOptions options, IReadOnlyList<ByteInterval> ignored)
    {
        private readonly List<ByteChange> _ranges = [];
        private int _ignoredIndex;
        private long _openStart = -1;
        private long _openEnd;
        private long _openDiffering;

        public IReadOnlyList<ByteChange> Ranges => _ranges;
        public long BeforeLength { get; private set; }
        public long AfterLength { get; private set; }
        public long ChangedBytes { get; private set; }
        public long IgnoredBytes { get; private set; }
        public int RangeCount { get; private set; }

        public void Run(Stream before, Stream after)
        {
            var a = new byte[BufferSize];
            var b = new byte[BufferSize];
            long position = 0;
            while (true)
            {
                var na = before.ReadAtLeast(a, a.Length, throwOnEndOfStream: false);
                var nb = after.ReadAtLeast(b, b.Length, throwOnEndOfStream: false);
                var common = Math.Min(na, nb);
                ScanBlock(a.AsSpan(0, common), b.AsSpan(0, common), position);
                position += common;
                if (na != nb)
                {
                    Close();
                    var longer = na > nb ? before : after;
                    var tail = Math.Max(na, nb) - common + Drain(longer, na > nb ? a : b);
                    AddTail(position, tail, na > nb ? ByteChangeKind.Truncated : ByteChangeKind.Appended);
                    BeforeLength = na > nb ? position + tail : position;
                    AfterLength = na > nb ? position : position + tail;
                    return;
                }

                if (na < a.Length)
                {
                    Close();
                    BeforeLength = AfterLength = position;
                    return;
                }
            }
        }

        private void ScanBlock(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, long blockStart)
        {
            var i = 0;
            while (i < a.Length)
            {
                // Vectorised skip over equal runs; only differing bytes are visited one by one.
                i += a[i..].CommonPrefixLength(b[i..]);
                if (i >= a.Length)
                {
                    return;
                }

                OnDifference(blockStart + i);
                i++;
            }
        }

        private void OnDifference(long offset)
        {
            if (IsIgnored(offset))
            {
                IgnoredBytes++;
                return;
            }

            ChangedBytes++;
            if (_openStart >= 0 && offset - _openEnd <= options.MergeGap)
            {
                _openEnd = offset + 1;
                _openDiffering++;
                return;
            }

            Close();
            _openStart = offset;
            _openEnd = offset + 1;
            _openDiffering = 1;
        }

        private bool IsIgnored(long offset)
        {
            while (_ignoredIndex < ignored.Count && ignored[_ignoredIndex].End <= offset)
            {
                _ignoredIndex++;
            }

            return _ignoredIndex < ignored.Count && ignored[_ignoredIndex].Start <= offset;
        }

        private void Close()
        {
            if (_openStart < 0)
            {
                return;
            }

            Record(new ByteChange(_openStart, _openEnd - _openStart, ByteChangeKind.Modified) { DifferingBytes = _openDiffering });
            _openStart = -1;
        }

        private void AddTail(long offset, long length, ByteChangeKind kind)
        {
            ChangedBytes += length;
            Record(new ByteChange(offset, length, kind) { DifferingBytes = length });
        }

        private void Record(ByteChange change)
        {
            RangeCount++;
            if (_ranges.Count < options.MaxRanges)
            {
                _ranges.Add(change);
            }
        }

        private static long Drain(Stream stream, byte[] buffer)
        {
            long total = 0;
            int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                total += read;
            }

            return total;
        }
    }
}
