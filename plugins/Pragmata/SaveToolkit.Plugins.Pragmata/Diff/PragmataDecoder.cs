using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Pragmata.Diff;

/// <summary>Read-only canonical evidence, independent of view expansion and physical offsets.</summary>
internal sealed class PragmataDecoder : ISaveDecoderHook
{
    public string Id => "pragmata-canonical-v1";
    public bool CanDecode(ISaveFile file) => new PragmataPlugin().Probe(file).IsMatch;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes)
        {
            sink.MarkTruncated("File exceeds decoder byte limit.");
            return;
        }
        var plaintext = new DsssContainer(file.ReadAllBytes()).Plaintext;
        Decode(plaintext, new SaveLayout(plaintext).Roots, sink);
    }

    internal void Decode(byte[] plaintext, IReadOnlyList<SaveNode> roots, ISaveDecodeSink sink)
    {
        foreach (var root in roots)
            if (!Walk(root, $"root[{root.Hash:x8}]", 0, plaintext, sink)) break;
    }

    private static bool Walk(SaveNode node, string path, int depth, byte[] bytes, ISaveDecodeSink sink)
    {
        if (depth > sink.MaxDepth)
        {
            sink.MarkTruncated($"Depth bound reached at {path}.");
            return true;
        }
        if (node.Type == FieldTags.Class)
        {
            if (!sink.Add(path + "/@class", "class", $"{node.ClassHash:x8}")) return false;
            foreach (var child in node.Children)
                if (!Walk(child, $"{path}/{child.Hash:x8}", depth + 1, bytes, sink)) return false;
            return true;
        }
        if (node.Type == FieldTags.Array)
        {
            if (!sink.Add(path + "/@count", "count", node.Children.Count.ToString(CultureInfo.InvariantCulture))) return false;
            var segments = RecordIdentities.ForArray(node, bytes, out var indexed);
            if (indexed)
                sink.MarkTruncated($"{path}: positional fallback; identity is unproven and reorder may appear as changes.");
            for (var i = 0; i < node.Children.Count; i++)
                if (!Walk(node.Children[i], path + segments[i], depth + 1, bytes, sink)) return false;
            return true;
        }
        // Strings/opaque payloads can contain identity; fingerprints detect changes without printing them.
        var value = node.IsPrimitive ? PrimitiveCodec.Read(node, bytes).ToString() :
            $"{node.Size} bytes; sha256={Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(node.Offset, node.Size)))}";
        return sink.Add(path, FieldTags.ShortName(node.Type), value, HashNames.All.GetValueOrDefault(node.Hash));
    }
}
