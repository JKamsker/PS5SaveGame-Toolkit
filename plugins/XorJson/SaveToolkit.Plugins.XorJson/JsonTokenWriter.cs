using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Json;

namespace SaveToolkit.Plugins.XorJson;

internal static class JsonTokenWriter
{
    public static byte[] Write(JsonValueToken root)
    {
        if (root.SdkDocument is { } document && IsUnchangedTree(root))
            return WriteScalars(document, root);
        using var output = new MemoryStream();
        Append(output, root.Source.AsSpan(0, root.Offset));
        WriteNode(output, root);
        Append(output, root.Source.AsSpan(root.Offset + root.Length));
        var result = output.ToArray();
        _ = JsonTokenTree.Parse(result);
        return result;
    }

    private static byte[] WriteScalars(LosslessJsonDocument document, JsonValueToken root)
    {
        try
        {
            foreach (var node in JsonEditModel.Walk(root).Where(node => !node.IsContainer))
            {
                if (node.SdkNode is null) throw new SaveFormatException("Edited JSON lost a scalar identity.");
                var lexeme = node.Current == node.Original
                    ? node.SdkNode.OriginalLexeme!
                    : Encoding.UTF8.GetString(node.Encode());
                _ = document.ReplaceScalar(node.SdkNode, lexeme);
            }
            var result = document.Serialize();
            _ = JsonTokenTree.Parse(result);
            return result;
        }
        catch (LosslessJsonException)
        {
            throw new SaveFormatException("Edited JSON exceeds the lossless scalar limits.");
        }
    }

    private static bool IsUnchangedTree(JsonValueToken root) => JsonEditModel.Walk(root).All(node =>
        node.SdkNode is not null && ReferenceEquals(node.Source, root.Source) &&
        (!node.IsContainer || node.OriginalChildren.SequenceEqual(node.Children.Select(child => child.Index))));

    private static void WriteNode(MemoryStream output, JsonValueToken node)
    {
        if (!node.IsContainer)
        {
            Append(output, node.Current == node.Original ? node.Source.AsSpan(node.Offset, node.Length) : node.Encode());
            return;
        }
        if (node.OriginalChildren.SequenceEqual(node.Children.Select(n => n.Index)) &&
            node.Children.All(n => ReferenceEquals(n.Source, node.Source)))
        {
            var position = node.Offset;
            foreach (var child in node.Children)
            {
                Append(output, node.Source.AsSpan(position, child.Offset - position));
                WriteNode(output, child);
                position = child.Offset + child.Length;
            }
            Append(output, node.Source.AsSpan(position, node.Offset + node.Length - position));
            return;
        }

        Append(output, node.Source.AsSpan(node.Offset, 1));
        for (var i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            if (i > 0) Append(output, ","u8);
            // Retain each member's original key spelling, colon and leading whitespace.
            var trivia = child.Source.AsSpan(child.LeadingOffset, child.EntryOffset - child.LeadingOffset);
            var comma = trivia.IndexOf((byte)',');
            Append(output, comma >= 0 ? trivia[(comma + 1)..] : trivia);
            Append(output, child.Source.AsSpan(child.EntryOffset, child.Offset - child.EntryOffset));
            WriteNode(output, child);
        }
        var end = node.Offset + node.Length - 1;
        var trailing = end;
        while (trailing > node.Offset && node.Source[trailing - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') trailing--;
        Append(output, node.Source.AsSpan(trailing, end - trailing + 1));
    }

    private static void Append(MemoryStream output, ReadOnlySpan<byte> bytes)
    {
        if (output.Length + bytes.Length > JsonTokenTree.MaxBytes)
            throw new SaveFormatException("Edited JSON exceeds the 16 MiB safety limit.");
        output.Write(bytes);
    }
}
