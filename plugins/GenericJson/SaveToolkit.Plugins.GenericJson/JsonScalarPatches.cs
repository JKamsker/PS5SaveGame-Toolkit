using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Uses shared scalar spans while leaving structural identities and snapshots local.</summary>
internal static class JsonScalarPatches
{
    public static bool TryWrite(JsonTokenTree tree, out byte[] bytes, out bool changed)
    {
        bytes = [];
        changed = false;
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
            if (node.IsContainer || !node.ValueChanged) continue;
            if (SameValue(node.Current, node.Original)) continue;
            var encoded = node.Encode();
            outputLength += encoded.Length - node.Length;
            replacements.Add((node.Index, encoded));
        }
        changed = replacements.Count != 0;
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

    private static bool SameValue(FieldValue current, FieldValue original)
    {
        if (current.Kind == FieldValueKind.Union && original.Kind == FieldValueKind.Union
            && ReferenceEquals(current.AsUnion(), original.AsUnion())) return true;
        if (current.Kind != original.Kind) return false;
        return current.Kind switch
        {
            FieldValueKind.Null => true,
            FieldValueKind.Bool => current.AsBool() == original.AsBool(),
            FieldValueKind.Text or FieldValueKind.ExactNumber => current.AsStringValue() == original.AsStringValue(),
            FieldValueKind.Union => current.AsUnion().Type == original.AsUnion().Type
                && SameValue(current.AsUnion().Value, original.AsUnion().Value),
            FieldValueKind.Integer => current.AsInteger() == original.AsInteger(),
            FieldValueKind.Float => current.AsDouble() == original.AsDouble(),
            _ => false,
        };
    }

    private static string AsStringValue(this FieldValue value) => value.Kind switch
    {
        FieldValueKind.Text => value.AsString(),
        FieldValueKind.ExactNumber => value.AsExactNumber(),
        _ => throw new InvalidOperationException("Expected text-like JSON value."),
    };
}
