using System.Text.Json;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Writes original token/trivia slices, replacing only explicit value, name and structure edits.</summary>
internal static class JsonTreeWriter
{
    private const long MaxRetainedSourceBytes = 32L * 1024 * 1024;

    public static byte[] WriteValue(JsonValueToken value) => Write(JsonTokenTree.FromValue(value));

    public static byte[] Write(JsonTokenTree tree)
    {
        if (tree.Nodes.Select(n => n.Source).Distinct().Sum(b => (long)b.Length) > MaxRetainedSourceBytes)
            throw new SaveFormatException("Structural edits retain too much source data (32 MiB limit); reopen the exported file first.");
        using var output = new MemoryStream();
        Append(tree.Preamble.Span);
        WriteNode(tree.Nodes[0]);
        Append(tree.Postamble.Span);
        var bytes = output.ToArray();
        var reparsed = JsonTokenTree.Parse(bytes);
        if (tree.Nodes.Count != reparsed.Nodes.Count)
            throw new SaveFormatException("Edited JSON changed its node count unexpectedly.");
        for (var i = 0; i < tree.Nodes.Count; i++)
        {
            var expected = tree.Nodes[i];
            var actual = reparsed.Nodes[i];
            if (expected.Label != actual.Label || expected.IsContainer != actual.IsContainer
                || (expected.IsContainer ? expected.Kind != actual.Kind : expected.Current != actual.Current))
                throw new SaveFormatException("Edited JSON failed token round-trip validation.");
        }
        return bytes;

        void Append(ReadOnlySpan<byte> span)
        {
            if (span.Length > JsonTokenTree.MaxBytes - output.Length)
                throw new SaveFormatException("Edited JSON exceeds the 16 MiB safety limit.");
            output.Write(span);
        }

        void WriteNode(JsonValueToken node)
        {
            if (!node.IsContainer)
            {
                Append(node.Current == node.Original ? node.Source.AsSpan(node.Offset, node.Length) : node.Encode());
                return;
            }
            Append(node.Source.AsSpan(node.Offset, 1));
            Append(node.Leading.Span);
            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                if (i > 0) Append(child.SeparatorBefore.IsEmpty ? node.DefaultSeparator.Span : child.SeparatorBefore.Span);
                if (node.Kind == JsonTokenType.StartObject)
                {
                    if (child.Label == child.OriginalLabel)
                        Append(child.Source.AsSpan(child.MemberOffset, child.Offset - child.MemberOffset));
                    else
                    {
                        Append(JsonSerializer.SerializeToUtf8Bytes(child.Label));
                        var triviaOffset = child.MemberOffset + child.MemberNameLength;
                        Append(child.Source.AsSpan(triviaOffset, child.Offset - triviaOffset));
                    }
                }
                WriteNode(child);
            }
            Append(node.Trailing.Span);
            Append(node.Source.AsSpan(node.Offset + node.Length - 1, 1));
        }
    }
}
