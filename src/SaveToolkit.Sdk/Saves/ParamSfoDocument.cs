using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SaveToolkit.Sdk.Saves;

/// <summary>
/// A bounded, lossless PSF/SFO document that can patch one existing UTF-8 value in place.
/// </summary>
public sealed class ParamSfoDocument
{
    private const uint Magic = 0x46535000;
    private const ushort FormatUtf8Special = 0x0004;
    private const ushort FormatUtf8 = 0x0204;
    private const int HeaderSize = 20;
    private const int IndexEntrySize = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] _bytes;
    private readonly ParamSfoPatchLimits _limits;
    private readonly ParamSfoEntry[] _entries;

    private ParamSfoDocument(byte[] bytes, ParamSfoPatchLimits limits, IReadOnlyList<ParamSfoEntry> entries)
    {
        _bytes = bytes;
        _limits = limits;
        _entries = entries.ToArray();
        Entries = Array.AsReadOnly(_entries);
        OriginalFileSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Original bytes, retained without normalization or resizing.</summary>
    public ReadOnlyMemory<byte> OriginalBytes => _bytes.ToArray();

    /// <summary>SHA-256 fingerprint of <see cref="OriginalBytes"/>.</summary>
    public string OriginalFileSha256 { get; }

    /// <summary>Entries in their original index-table order.</summary>
    public IReadOnlyList<ParamSfoEntry> Entries { get; }

    /// <summary>Parses a bounded SFO document and retains every source byte.</summary>
    public static ParamSfoDocument Parse(ReadOnlySpan<byte> bytes, ParamSfoPatchLimits? limits = null)
    {
        limits ??= ParamSfoPatchLimits.Default;
        if (bytes.Length > limits.MaxFileBytes)
            throw Error($"SFO is {bytes.Length} bytes, exceeding MaxFileBytes {limits.MaxFileBytes}.");
        if (bytes.Length < HeaderSize)
            throw Error("SFO header is truncated.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
            throw Error("SFO magic is invalid.");

        var keyTable = ReadInt32(bytes, 8, "key table offset");
        var dataTable = ReadInt32(bytes, 12, "data table offset");
        var count = ReadInt32(bytes, 16, "entry count");
        if (count < 0 || count > (bytes.Length - HeaderSize) / IndexEntrySize)
            throw Error("SFO entry count exceeds the index table.");

        var indexEnd = HeaderSize + (count * IndexEntrySize);
        if (keyTable < indexEnd || keyTable > bytes.Length || dataTable < keyTable || dataTable > bytes.Length)
            throw Error("SFO table offsets overlap or exceed the source bytes.");
        if ((keyTable & 3) != 0 || (dataTable & 3) != 0)
            throw Error("SFO table offsets are not four-byte aligned.");

        var keyTableLength = dataTable - keyTable;
        var keyTerminatorByOffset = FindKeyTerminators(bytes, keyTable, keyTableLength);
        var layouts = new List<EntryLayout>(count);
        var keyRanges = new List<(int Start, int End, int Index)>(count);
        var slots = new List<(int Start, int End, int Index)>(count);
        for (var index = 0; index < count; index++)
        {
            var indexStart = HeaderSize + (index * IndexEntrySize);
            var indexBytes = bytes.Slice(indexStart, IndexEntrySize);
            var keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(indexBytes);
            var format = BinaryPrimitives.ReadUInt16LittleEndian(indexBytes[2..]);
            var length = ReadInt32(indexBytes, 4, $"entry {index} length");
            var maximumLength = ReadInt32(indexBytes, 8, $"entry {index} maximum length");
            var dataOffset = ReadInt32(indexBytes, 12, $"entry {index} data offset");
            if (length < 0 || maximumLength < 0 || length > maximumLength)
                throw Error($"Entry {index} has an invalid logical or reserved length.");
            if (maximumLength > limits.MaxEntryBytes)
                throw Error($"Entry {index} exceeds MaxEntryBytes {limits.MaxEntryBytes}.");
            if (keyOffset >= keyTableLength)
                throw Error($"Entry {index} key offset exceeds the key table.");
            if (dataOffset < 0 || dataOffset > bytes.Length - dataTable || maximumLength > bytes.Length - dataTable - dataOffset)
                throw Error($"Entry {index} data slot exceeds the source bytes.");

            var keyStart = keyTable + keyOffset;
            var keyTerminator = keyTerminatorByOffset[keyOffset];
            if (keyTerminator < 0 || keyTerminator - keyOffset > limits.MaxKeyBytes)
                throw Error($"Entry {index} key is missing its terminator or exceeds MaxKeyBytes.");
            var keyLength = keyTerminator - keyOffset;
            var keyEnd = keyStart + keyLength + 1;
            keyRanges.Add((keyStart, keyEnd, index));
            var valueStart = dataTable + dataOffset;
            slots.Add((valueStart, valueStart + maximumLength, index));
            layouts.Add(new EntryLayout(
                index, indexStart, keyOffset, format, length, maximumLength, dataOffset,
                keyStart, keyLength, valueStart));
        }

        ValidateNonOverlappingRanges(keyRanges, "key");
        ValidateNonOverlappingRanges(slots, "data slot");

        var entries = new List<ParamSfoEntry>(count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layout in layouts)
        {
            var rawIndex = bytes.Slice(layout.IndexStart, IndexEntrySize).ToArray();
            var keyBytes = bytes.Slice(layout.KeyStart, layout.KeyLength).ToArray();
            var key = Encoding.ASCII.GetString(keyBytes);
            if (!keys.Add(key))
                throw Error($"SFO contains duplicate key '{key}'.");
            var valueBytes = bytes.Slice(layout.ValueStart, layout.Length).ToArray();
            var reservedBytes = bytes.Slice(layout.ValueStart, layout.MaximumLength).ToArray();
            entries.Add(new ParamSfoEntry(
                layout.Index, key, layout.KeyOffset, layout.Format, layout.Length, layout.MaximumLength,
                layout.DataOffset, keyBytes, valueBytes, reservedBytes, rawIndex,
                ComputeEntryFingerprint(rawIndex, keyBytes, reservedBytes)));
        }

        return new ParamSfoDocument(bytes.ToArray(), limits, entries);
    }

    /// <summary>Plans a strict UTF-8 replacement for one unique existing string entry.</summary>
    public ParamSfoPatchPlan PlanExistingUtf8(string key, string newText)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(newText);
        if (newText.Contains('\0', StringComparison.Ordinal))
            throw Error($"Replacement for '{key}' contains an embedded NUL.");

        var entry = _entries.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
        if (entry is null) throw Error($"SFO key '{key}' does not exist.");
        if (entry.Format is not (FormatUtf8 or FormatUtf8Special))
            throw Error($"SFO key '{key}' has unsupported format 0x{entry.Format:X4}.");

        var originalText = DecodeOriginalText(entry);
        byte[] encoded;
        try
        {
            encoded = StrictUtf8.GetBytes(newText);
        }
        catch (EncoderFallbackException exception)
        {
            throw Error($"Replacement for '{key}' is not valid UTF-8: {exception.Message}");
        }
        ValidateReplacementLength(key, encoded.Length, entry.MaximumLength);

        return new ParamSfoPatchPlan(
            key, entry.Format, entry.Index, entry.DataOffset, entry.Length, entry.MaximumLength,
            originalText, newText, OriginalFileSha256, entry.EntrySha256);
    }

    /// <summary>Applies a previously planned replacement without mutating this document.</summary>
    public ReadOnlyMemory<byte> Apply(ParamSfoPatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(plan.OriginalFileSha256, OriginalFileSha256, StringComparison.OrdinalIgnoreCase))
            throw Error("The patch plan does not match this SFO source fingerprint.");
        if (plan.EntryIndex < 0 || plan.EntryIndex >= _entries.Length)
            throw Error("The patch plan entry index is outside this SFO document.");
        var entry = _entries[plan.EntryIndex];
        if (!string.Equals(plan.Key, entry.Key, StringComparison.Ordinal)
            || plan.EntryIndex != entry.Index
            || plan.Format != entry.Format
            || plan.DataOffset != entry.DataOffset
            || plan.OriginalLength != entry.Length
            || plan.MaximumLength != entry.MaximumLength
            || !string.Equals(plan.OriginalEntrySha256, entry.EntrySha256, StringComparison.OrdinalIgnoreCase))
            throw Error("The patch plan entry identity does not match this SFO document.");
        if (entry.Format is not (FormatUtf8 or FormatUtf8Special))
            throw Error($"SFO key '{entry.Key}' has unsupported format 0x{entry.Format:X4}.");
        if (!string.Equals(plan.OriginalText, DecodeOriginalText(entry), StringComparison.Ordinal))
            throw Error("The patch plan original text does not match this SFO document.");
        if (plan.NewText is null)
            throw Error("The patch plan replacement text cannot be null.");
        if (plan.NewText.Contains('\0', StringComparison.Ordinal))
            throw Error("The patch plan replacement contains an embedded NUL.");

        byte[] encoded;
        try { encoded = StrictUtf8.GetBytes(plan.NewText); }
        catch (EncoderFallbackException exception) { throw Error($"Patch plan replacement is not valid UTF-8: {exception.Message}"); }
        ValidateReplacementLength(entry.Key, encoded.Length, entry.MaximumLength);
        if (plan.IsNoOp) return _bytes.ToArray();

        var output = _bytes.ToArray();
        var indexStart = HeaderSize + (entry.Index * IndexEntrySize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(indexStart + 4), encoded.Length + 1);
        var valueStart = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(12)) + entry.DataOffset;
        encoded.CopyTo(output.AsSpan(valueStart));
        output[valueStart + encoded.Length] = 0;
        return output;
    }

    private string DecodeOriginalText(ParamSfoEntry entry)
    {
        var raw = entry.ValueBytes.Span;
        var terminator = raw.IndexOf((byte)0);
        if (terminator < 0 || terminator != raw.Length - 1)
            throw Error($"SFO key '{entry.Key}' has an invalid UTF-8 value terminator.");
        try { return StrictUtf8.GetString(raw[..terminator]); }
        catch (DecoderFallbackException exception) { throw Error($"SFO key '{entry.Key}' is not valid UTF-8: {exception.Message}"); }
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, int offset, string label)
    {
        if (offset < 0 || offset > data.Length - sizeof(int)) throw Error($"SFO {label} is truncated.");
        return BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    }

    private static int[] FindKeyTerminators(ReadOnlySpan<byte> bytes, int keyTable, int keyTableLength)
    {
        var terminators = new int[keyTableLength];
        var nextTerminator = -1;
        for (var offset = keyTableLength - 1; offset >= 0; offset--)
        {
            if (bytes[keyTable + offset] == 0) nextTerminator = offset;
            terminators[offset] = nextTerminator;
        }

        return terminators;
    }

    private static string ComputeEntryFingerprint(byte[] rawIndex, byte[] keyBytes, byte[] reservedBytes)
    {
        var input = new byte[rawIndex.Length + keyBytes.Length + reservedBytes.Length];
        rawIndex.CopyTo(input, 0);
        keyBytes.CopyTo(input, rawIndex.Length);
        reservedBytes.CopyTo(input, rawIndex.Length + keyBytes.Length);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private static void ValidateNonOverlappingRanges(
        List<(int Start, int End, int Index)> ranges,
        string rangeName)
    {
        ranges.Sort(static (left, right) =>
        {
            var start = left.Start.CompareTo(right.Start);
            if (start != 0) return start;
            var end = left.End.CompareTo(right.End);
            return end != 0 ? end : left.Index.CompareTo(right.Index);
        });

        var maximumEnd = int.MinValue;
        var maximumIndex = -1;
        foreach (var range in ranges)
        {
            if (range.Start == range.End) continue;
            if (range.Start < maximumEnd)
                throw Error($"SFO {rangeName} ranges for entries {maximumIndex} and {range.Index} overlap.");
            if (range.End > maximumEnd)
            {
                maximumEnd = range.End;
                maximumIndex = range.Index;
            }
        }
    }

    private void ValidateReplacementLength(string key, int payloadLength, int maximumLength)
    {
        if (payloadLength > _limits.MaxEncodedValueBytes - 1)
            throw Error($"Replacement for '{key}' including its terminator exceeds MaxEncodedValueBytes {_limits.MaxEncodedValueBytes}.");
        if (payloadLength > maximumLength - 1)
            throw Error($"Replacement for '{key}' including its terminator exceeds the {maximumLength}-byte reserved slot.");
    }

    private static ParamSfoPatchException Error(string message) => new(message);

    private readonly record struct EntryLayout(
        int Index,
        int IndexStart,
        ushort KeyOffset,
        ushort Format,
        int Length,
        int MaximumLength,
        int DataOffset,
        int KeyStart,
        int KeyLength,
        int ValueStart);
}
