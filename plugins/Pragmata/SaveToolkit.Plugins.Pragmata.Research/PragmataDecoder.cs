using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Research;

/// <summary>F1 decoder independent of view expansion, edit registration and physical offsets.</summary>
public sealed class PragmataDecoder : ISaveDecoder
{
    public string Id => "pragmata-canonical-v1";
    public bool CanDecode(ISaveFile file) => new PragmataPlugin().Probe(file).IsMatch;

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes)
            return new SemanticTree(Id, []) { Truncated = true, Warnings = ["File exceeds decoder byte limit."] };
        var plaintext = new DsssContainer(file.ReadAllBytes()).Plaintext;
        return Decode(plaintext, new SaveLayout(plaintext).Roots, limits);
    }

    internal SemanticTree Decode(byte[] plaintext, IReadOnlyList<SaveNode> roots, DecodeLimits limits)
    {
        var builder = new SemanticTreeBuilder(Id, limits);
        foreach (var root in roots)
            if (!Walk(root, $"root[{root.Hash:x8}]", 0, plaintext, builder)) break;
        return builder.Build();
    }

    private static bool Walk(SaveNode node, string path, int depth, byte[] bytes, SemanticTreeBuilder builder)
    {
        if (depth > builder.Limits.MaxDepth)
        {
            builder.MarkTruncated($"Depth bound reached at {path}.");
            return true;
        }
        if (node.Type == FieldTags.Class)
        {
            if (!builder.Add(path + "/@class", "class", $"{node.ClassHash:x8}")) return false;
            foreach (var child in node.Children)
                if (!Walk(child, $"{path}/{child.Hash:x8}", depth + 1, bytes, builder)) return false;
            return true;
        }
        if (node.Type == FieldTags.Array)
        {
            if (!builder.Add(path + "/@count", "count", node.Children.Count.ToString(CultureInfo.InvariantCulture))) return false;
            var segments = RecordIdentities.ForArray(node, bytes, out var indexed);
            if (indexed) builder.Warn($"{path}: positional fallback; identity is unproven and reorder may appear as changes.");
            for (var i = 0; i < node.Children.Count; i++)
                if (!Walk(node.Children[i], path + segments[i], depth + 1, bytes, builder)) return false;
            return true;
        }
        // Never print strings/opaque payloads (they can contain user identity); full hashes detect changes.
        var value = node.IsPrimitive ? PrimitiveCodec.Read(node, bytes).ToString() :
            $"{node.Size} bytes; sha256={Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(node.Offset, node.Size)))}";
        return builder.Add(path, FieldTags.ShortName(node.Type), value, HashNames.All.GetValueOrDefault(node.Hash));
    }
}
