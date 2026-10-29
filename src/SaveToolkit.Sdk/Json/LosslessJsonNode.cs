using System.Collections.ObjectModel;

namespace SaveToolkit.Sdk.Json;

/// <summary>One value in a <see cref="LosslessJsonDocument"/> lexical tree.</summary>
public sealed class LosslessJsonNode
{
    private readonly List<LosslessJsonNode> _children = [];
    private readonly ReadOnlyCollection<LosslessJsonNode> _childrenView;

    internal LosslessJsonNode(int id, LosslessJsonNodeKind kind, JsonByteSpan span)
    {
        Id = id;
        Kind = kind;
        OriginalSpan = span;
        _childrenView = new ReadOnlyCollection<LosslessJsonNode>(_children);
    }

    /// <summary>Stable preorder identity assigned when the document is parsed.</summary>
    public int Id { get; }
    /// <summary>Current JSON value kind.</summary>
    public LosslessJsonNodeKind Kind { get; internal set; }
    /// <summary>Original byte span, including string quotes or container delimiters.</summary>
    public JsonByteSpan OriginalSpan { get; internal set; }
    /// <summary>Parent value, or null for the root.</summary>
    public LosslessJsonNode? Parent { get; internal set; }
    /// <summary>Object member name decoded as Unicode, or null for array/root values.</summary>
    public string? PropertyName { get; internal set; }
    /// <summary>Original member-name token, including quotes and escapes.</summary>
    public string? PropertyNameLexeme { get; internal set; }
    /// <summary>Original member-name token span, including quotes.</summary>
    public JsonByteSpan? PropertyNameSpan { get; internal set; }
    /// <summary>Zero-based occurrence among equal decoded names in this object.</summary>
    public int PropertyOccurrence { get; internal set; }
    /// <summary>Decoded string value for string scalars; null for other kinds.</summary>
    public string? StringValue { get; internal set; }
    /// <summary>Decoded boolean value for boolean scalars; null for other kinds.</summary>
    public bool? BooleanValue { get; internal set; }
    /// <summary>Whether the current scalar is JSON null.</summary>
    public bool IsNull => Kind == LosslessJsonNodeKind.Null;
    /// <summary>Original scalar token lexeme, including quotes for strings.</summary>
    public string? OriginalLexeme { get; internal set; }
    /// <summary>Current scalar token lexeme, or null for containers.</summary>
    public string? CurrentLexeme { get; internal set; }
    /// <summary>Whether this node can be patched with <see cref="LosslessJsonDocument.ReplaceScalar(LosslessJsonNode, string)"/>.</summary>
    public bool IsScalar => Kind is not (LosslessJsonNodeKind.Object or LosslessJsonNodeKind.Array);
    /// <summary>Child values in source order.</summary>
    public IReadOnlyList<LosslessJsonNode> Children => _childrenView;

    internal List<LosslessJsonNode> MutableChildren => _children;
}

/// <summary>Immutable replacement state captured for lossless JSON undo/redo.</summary>
public sealed class LosslessJsonSnapshot
{
    internal LosslessJsonSnapshot(object owner, IReadOnlyDictionary<int, string> replacements, long sizeBytes)
    {
        Owner = owner;
        Replacements = replacements;
        SizeBytes = sizeBytes;
    }

    internal object Owner { get; }

    /// <summary>Scalar lexemes keyed by stable node identity.</summary>
    public IReadOnlyDictionary<int, string> Replacements { get; }
    /// <summary>Approximate retained memory for this snapshot.</summary>
    public long SizeBytes { get; }
}
