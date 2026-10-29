using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Json;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Uses shared scalar spans while leaving structural identities and snapshots local.</summary>
internal static class JsonScalarPatches
{
    public static bool TryWrite(JsonTokenTree tree, out byte[] bytes)
    {
        bytes = [];
        if (tree.LexicalSource is not { } source || tree.Nodes.Count != source.Nodes.Count) return false;
        for (var i = 0; i < tree.Nodes.Count; i++)
        {
            var node = tree.Nodes[i];
            if (!node.Imported || node.Index != i || node.Label != node.OriginalLabel || !node.HasOriginalChildren)
                return false;
        }

        var replacements = new List<(int Id, byte[] Encoded)>();
        long outputLength = source.OriginalLength;
        foreach (var node in tree.Nodes)
        {
            if (node.IsContainer || node.Current == node.Original) continue;
            var encoded = node.Encode();
            outputLength += encoded.Length - node.Length;
            replacements.Add((node.Index, encoded));
        }
        if (outputLength > JsonTokenTree.MaxBytes)
            throw new SaveFormatException("Edited JSON exceeds the 16 MiB safety limit.");

        // Tree snapshots share immutable source spans. Scope scalar replacements to this export;
        // plugin structural history remains independent of this temporary SDK snapshot.
        lock (source)
        {
            var original = source.CaptureSnapshot();
            try
            {
                // Shrink first so a valid atomic batch cannot exceed the byte limit midway.
                foreach (var replacement in replacements.OrderBy(replacement =>
                    replacement.Encoded.Length - source.Nodes[replacement.Id].OriginalSpan.Length))
                {
                    source.ReplaceScalar(replacement.Id, Encoding.UTF8.GetString(replacement.Encoded));
                }
                bytes = source.Serialize();
                return true;
            }
            catch (LosslessJsonException ex)
            {
                throw new SaveFormatException("Edited JSON failed shared scalar validation.", ex);
            }
            finally
            {
                source.RestoreSnapshot(original);
            }
        }
    }
}
