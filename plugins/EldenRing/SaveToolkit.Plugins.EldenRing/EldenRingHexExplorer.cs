using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class EldenRingHexExplorer
{
    private const int MaximumPageSize = 64 * 1024;
    private readonly byte[] _bytes;
    private readonly Dictionary<string, BlobRange> _ranges;

    public EldenRingHexExplorer(byte[] bytes, ParsedSave save)
    {
        _bytes = bytes;
        _ranges = save.Slots.ToDictionary(slot => $"raw/slots/{slot.Index}",
            slot => new BlobRange($"Slot {slot.Index} · 0x{slot.Offset:X}", slot.Offset, EldenRingLayout.SlotSize));
        _ranges.Add("raw/header", new BlobRange("PlayStation header", 0, EldenRingLayout.HeaderSize));
        _ranges.Add("raw/profile", new BlobRange("User data / profile summaries", EldenRingLayout.UserDataOffset, EldenRingLayout.UserDataSize));
        _ranges.Add("raw/regulation", new BlobRange("Regulation / remaining bytes", EldenRingLayout.RegulationOffset,
            bytes.Length - EldenRingLayout.RegulationOffset));
    }

    public IReadOnlyList<ViewNode> BuildSections() => _ranges
        .Select(pair => (ViewNode)SectionNode.Lazy(pair.Value.Title, pair.Key)).ToArray();

    public IReadOnlyList<ViewNode> Expand(string id)
    {
        var range = GetRange(id);
        return [new HexNode(id, range.Length) { Title = range.Title }];
    }

    public ReadOnlyMemory<byte> Read(string id, long offset, int count)
    {
        var range = GetRange(id);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, range.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaximumPageSize);
        var length = Math.Min(count, range.Length - (int)offset);
        // Return a copy: a consumer cannot acquire and mutate the document's backing array.
        return _bytes.AsSpan(range.Offset + (int)offset, length).ToArray();
    }

    private BlobRange GetRange(string id) => _ranges.TryGetValue(id, out var range)
        ? range : throw new KeyNotFoundException($"Unknown raw section '{id}'.");

    private sealed record BlobRange(string Title, int Offset, int Length);
}
