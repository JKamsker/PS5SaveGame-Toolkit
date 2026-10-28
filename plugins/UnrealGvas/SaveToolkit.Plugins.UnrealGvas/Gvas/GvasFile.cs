using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

/// <summary>Lossless tagged GVAS model, independent of the view protocol. All unedited bytes are retained.</summary>
public sealed class GvasFile
{
    public const int MaxNodes = 100_000;
    public const int MaxStringLength = 1_048_576;
    public const int MaxFileLength = 128 * 1024 * 1024;
    internal const int MaxDepth = 64;
    private readonly byte[] _original;
    private readonly IReadOnlyList<SizeRegion> _sizes;
    public GvasHeader Header { get; }
    public IReadOnlyList<GvasNode> Properties { get; }
    public ReadOnlyMemory<byte> Original => _original;
    public bool HasChanges => Descendants(Properties).Any(n => n.Scalar?.Changed == true);

    internal GvasFile(byte[] original, GvasHeader header, IReadOnlyList<GvasNode> properties, IReadOnlyList<SizeRegion> sizes)
    {
        _original = original;
        Header = header;
        Properties = properties;
        _sizes = sizes;
    }

    public static GvasFile Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxFileLength) throw new SaveFormatException("GVAS exceeds the 128 MiB safety limit.");
        return new GvasParser(bytes.ToArray()).Parse();
    }

    /// <summary>
    /// Parses a bounded legacy property stream that has no GVAS header. Unreal stores some
    /// compressed actor arrays this way. The supplied header controls the tag layout; the returned
    /// model is inspection-only and must not be serialized as a standalone save.
    /// </summary>
    public static GvasFile ParseHeaderless(ReadOnlySpan<byte> bytes, GvasHeader header)
    {
        if (bytes.Length > MaxFileLength) throw new SaveFormatException("Headerless property stream exceeds the 128 MiB safety limit.");
        return new GvasParser(bytes.ToArray()).ParseHeaderless(header);
    }

    public static IEnumerable<GvasNode> Descendants(IEnumerable<GvasNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descendants(node.Children)) yield return child;
        }
    }

    public byte[] Serialize()
    {
        return SerializeReplacements(new Dictionary<GvasNode, ReadOnlyMemory<byte>>());
    }

    /// <summary>
    /// Serializes scalar edits and complete opaque payload replacements while preserving every
    /// unedited byte. Replacements are used by title-specific codecs whose payload length can grow.
    /// </summary>
    public byte[] SerializeReplacements(IReadOnlyDictionary<GvasNode, ReadOnlyMemory<byte>> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        var patches = Descendants(Properties).Where(n => n.Scalar?.Changed == true)
            .Select(n => n.Scalar!).Select(s => new BytePatch(s.Offset, s.Length, s.Encode())).ToList();
        foreach (var (node, bytes) in replacements)
        {
            if (!Descendants(Properties).Contains(node)) throw new SaveFormatException("Replacement node is not part of this GVAS.");
            if (node.Scalar is not null) throw new SaveFormatException("Scalar payloads must be edited through their field binding.");
            if (bytes.Length > MaxFileLength) throw new SaveFormatException("Replacement payload exceeds the 128 MiB safety limit.");
            patches.Add(new BytePatch(node.Offset, node.Length, bytes.ToArray()));
        }
        var valuePatches = patches.ToArray();
        foreach (var region in _sizes)
        {
            var delta = valuePatches.Where(p => p.Offset >= region.Start && p.Offset + p.Length <= region.Start + region.Length)
                .Sum(p => p.Bytes.Length - p.Length);
            if (delta == 0) continue;
            var size = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(size, checked(region.Length + delta));
            patches.Add(new BytePatch(region.SizeOffset, 4, size));
        }
        var outputLength = checked(_original.Length + valuePatches.Sum(p => p.Bytes.Length - p.Length));
        if (outputLength > MaxFileLength) throw new SaveFormatException("Edited GVAS exceeds the 128 MiB safety limit.");
        using var output = new MemoryStream(outputLength);
        var position = 0;
        foreach (var patch in patches.OrderBy(p => p.Offset))
        {
            if (patch.Offset < position) throw new SaveFormatException("Overlapping GVAS edits.");
            output.Write(_original.AsSpan(position, patch.Offset - position));
            output.Write(patch.Bytes);
            position = patch.Offset + patch.Length;
        }
        output.Write(_original.AsSpan(position));
        var result = output.ToArray();
        var reparsed = Parse(result);
        var expected = Descendants(Properties).ToArray();
        var actual = Descendants(reparsed.Properties).ToArray();
        if (expected.Length != actual.Length) throw new SaveFormatException("Edited GVAS changed its parsed structure.");
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i].Name != actual[i].Name || expected[i].Type != actual[i].Type ||
                (expected[i].Scalar is { } scalar && !scalar.Value.Equals(actual[i].Scalar?.Value)))
                throw new SaveFormatException($"Edited GVAS failed validation at {expected[i].Name}.");
        }
        return result;
    }
}
