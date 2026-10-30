using System.Globalization;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

internal sealed class JsonTokenTree
{
    internal const int MaxBytes = 16 * 1024 * 1024;
    internal const int MaxNodes = 250_000;
    internal const int MaxDepth = 64;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal LosslessJsonDocument? LexicalSource { get; private set; }

    public List<JsonValueToken> Nodes { get; } = [];
    public Dictionary<int, JsonValueToken> ById { get; } = [];
    public ReadOnlyMemory<byte> Preamble { get; private set; }
    public ReadOnlyMemory<byte> Postamble { get; private set; }
    public int NextId { get; set; }

    public JsonValueToken? FindValue(string valueId) => valueId.StartsWith("value/", StringComparison.Ordinal)
        && int.TryParse(valueId.AsSpan("value/".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && ById.TryGetValue(id, out var node) && node.ValueId == valueId ? node : null;

    public static JsonTokenTree Parse(string text)
    {
        try { return Parse(StrictUtf8.GetBytes(text)); }
        catch (EncoderFallbackException)
        {
            throw new SaveFormatException("JSON text contains an unpaired Unicode surrogate.");
        }
    }

    public static JsonTokenTree Parse(byte[] bytes)
    {
        if (bytes.Length is <= 0 or > MaxBytes)
        {
            throw new SaveFormatException("JSON files must be between 1 byte and 16 MiB.");
        }

        LosslessJsonDocument source;
        try
        {
            source = LosslessJsonDocument.Parse(bytes, new LosslessJsonLimits
            {
                MaxBytes = MaxBytes, MaxDepth = MaxDepth, MaxTokens = MaxNodes,
                MaxNumberLength = ExactJsonNumber.HardMaxLength,
                MaxAbsoluteExponent = ExactJsonNumber.HardMaxAbsoluteExponent,
            });
        }
        catch (LosslessJsonException ex)
        {
            // Preserve the established streaming diagnostic text and failure precedence.
            // Successful imports use only the shared lexer; this path creates no editable tree.
            JsonTokenValidation.Validate(bytes);
            throw new SaveFormatException("JSON could not be represented by the shared lexical tree.", ex);
        }
        var tree = new JsonTokenTree { LexicalSource = source, Preamble = source.Preamble, Postamble = source.Postamble };
        foreach (var lexical in source.Nodes)
        {
            var parent = lexical.Parent is null ? null : tree.Nodes[lexical.Parent.Id];
            var node = new JsonValueToken(lexical.Id, lexical.PropertyName ?? (parent is null ? "$" : $"[{parent.Children.Count}]"),
                TokenKind(lexical), lexical.OriginalSpan.Offset, lexical.OriginalSpan.Length)
            {
                Source = bytes, Parent = parent,
                MemberOffset = lexical.PropertyNameSpan?.Offset ?? lexical.OriginalSpan.Offset,
                MemberNameLength = lexical.PropertyNameSpan?.Length ?? 0,
                Position = parent?.Children.Count ?? 0,
            };
            tree.Nodes.Add(node);
            parent?.Children.Add(node);
            if (!node.IsContainer) ReadValue(node, lexical);
        }
        foreach (var node in tree.Nodes.Where(node => node.IsContainer))
            node.OriginalChildIds = Array.AsReadOnly(node.Children.Select(child => child.Index).ToArray());
        foreach (var node in tree.Nodes) node.BeginTracking();
        tree.InitializeSpans(bytes);
        tree.NextId = tree.Nodes.Count;
        tree.Reindex();
        return tree;
    }

    private static JsonTokenType TokenKind(LosslessJsonNode node) => node.Kind switch
    {
        LosslessJsonNodeKind.Object => JsonTokenType.StartObject,
        LosslessJsonNodeKind.Array => JsonTokenType.StartArray,
        LosslessJsonNodeKind.String => JsonTokenType.String,
        LosslessJsonNodeKind.Number => JsonTokenType.Number,
        LosslessJsonNodeKind.Boolean => node.BooleanValue == true ? JsonTokenType.True : JsonTokenType.False,
        LosslessJsonNodeKind.Null => JsonTokenType.Null,
        _ => throw new InvalidOperationException("Unknown JSON lexical kind."),
    };

    private void InitializeSpans(byte[] bytes)
    {
        var root = Nodes[0];
        Preamble = bytes.AsMemory(0, root.Offset);
        Postamble = bytes.AsMemory(root.Offset + root.Length);
        foreach (var container in Nodes.Where(n => n.IsContainer))
        {
            var start = container.Offset + 1;
            var end = container.Offset + container.Length - 1;
            var previousEnd = start;
            foreach (var child in container.Children)
            {
                if (previousEnd == start) container.Leading = bytes.AsMemory(start, child.MemberOffset - start);
                else child.SeparatorBefore = bytes.AsMemory(previousEnd, child.MemberOffset - previousEnd);
                previousEnd = child.Offset + child.Length;
            }
            if (container.Children.Count == 0) container.Leading = bytes.AsMemory(start, end - start);
            else container.Trailing = bytes.AsMemory(previousEnd, end - previousEnd);
            container.DefaultSeparator = container.Children.Skip(1).FirstOrDefault()?.SeparatorBefore
                ?? new byte[] { (byte)',' }.Concat(container.Leading.ToArray()).ToArray();
        }
    }

    public void Reindex()
    {
        var root = Nodes[0];
        Nodes.Clear();
        ById.Clear();
        Visit(root, null);
        void Visit(JsonValueToken node, JsonValueToken? parent)
        {
            node.Parent = parent;
            node.TreeOrder = Nodes.Count;
            // All membership, order and name mutations pass through Reindex, including snapshot restore.
            node.StructureReview = null;
            node.EscapedPathLabel = null;
            node.FriendlyLabel = null;
            Nodes.Add(node);
            ById.Add(node.Index, node);
            var occurrences = node.Kind == JsonTokenType.StartObject ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                if (node.Kind == JsonTokenType.StartArray && (child.Position != i || child.Parent != node)) child.Label = $"[{i}]";
                child.Position = i;
                child.Occurrence = 1;
                if (occurrences is not null)
                {
                    occurrences.TryGetValue(child.Label, out var previous);
                    child.Occurrence = previous + 1;
                    occurrences[child.Label] = child.Occurrence;
                }
                Visit(node.Children[i], node);
            }
            // Count every sibling, including entries beyond the title adapter's bounded prefix.
            foreach (var child in node.Children)
                child.UniqueMember = occurrences is null || occurrences[child.Label] == 1;
        }
    }

    public JsonTokenTree Clone()
    {
        var clone = new JsonTokenTree { Preamble = Preamble, Postamble = Postamble, NextId = NextId, LexicalSource = LexicalSource };
        clone.Nodes.Add(Nodes[0].Clone());
        clone.Reindex();
        return clone;
    }

    public static JsonTokenTree FromValue(JsonValueToken value)
    {
        var tree = new JsonTokenTree();
        var root = value.Clone();
        root.Label = "$";
        tree.Nodes.Add(root);
        tree.Reindex();
        return tree;
    }

    public long EstimateBytes()
    {
        const int NodeOverhead = 320;
        const int LexicalNodeOverhead = 200;
        // Conservatively charge shared source storage to each snapshot, including source nodes
        // removed from the live structural tree. History must not undercount retained originals.
        var lexicalBytes = LexicalSource is { } source ? source.OriginalLength + source.Nodes.Sum(node =>
            LexicalNodeOverhead + 2L * ((node.OriginalLexeme?.Length ?? 0)
                + (node.PropertyName?.Length ?? 0) + (node.PropertyNameLexeme?.Length ?? 0))) : 0;
        return Nodes.Select(n => n.Source).Distinct().Sum(b => (long)b.Length)
            + Nodes.Sum(n => NodeOverhead + (long)sizeof(int) * n.OriginalChildIds.Count
                + 2L * (n.Label.Length + n.OriginalLabel.Length + n.Original.ToString().Length + n.Current.ToString().Length))
            + lexicalBytes;
    }

    private static void ReadValue(JsonValueToken node, LosslessJsonNode lexical)
    {
        if (lexical.Kind == LosslessJsonNodeKind.Number)
        {
            var raw = lexical.OriginalLexeme!;
            if (!WithinNumberResources(raw))
            {
                node.Spec = new TextSpec { ReadOnly = true };
                node.Current = FieldValue.Of(raw);
                node.Original = node.Current;
                node.Warning = "Number exceeds SDK lexeme/exponent resource limits; preserved verbatim and read-only.";
                return;
            }
            node.Spec = JsonScalar.Spec;
            node.Current = FieldValue.Union(JsonScalar.Number, FieldValue.ExactNumber(raw));
            node.Original = node.Current;
            return;
        }

        node.Spec = JsonScalar.Spec;
        node.Current = lexical.Kind switch
        {
            LosslessJsonNodeKind.Null => FieldValue.Null,
            LosslessJsonNodeKind.String => FieldValue.Union(JsonScalar.Text, FieldValue.Of(lexical.StringValue!)),
            LosslessJsonNodeKind.Boolean => FieldValue.Union(JsonScalar.Boolean, FieldValue.Of(lexical.BooleanValue!.Value)),
            _ => throw new InvalidOperationException("Expected a JSON scalar."),
        };
        node.Original = node.Current;
    }

    private static bool WithinNumberResources(string raw)
    {
        // The shared lexer has already checked syntax. FieldValue.ExactNumber validates accepted
        // lexemes again; only decide whether to use the lossless read-only resource fallback here.
        if (raw.Length > ExactJsonNumber.HardMaxLength) return false;
        var exponentAt = raw.AsSpan().IndexOfAny('e', 'E');
        return exponentAt < 0 || int.TryParse(raw.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var exponent)
            && exponent >= -ExactJsonNumber.HardMaxAbsoluteExponent && exponent <= ExactJsonNumber.HardMaxAbsoluteExponent;
    }
}
