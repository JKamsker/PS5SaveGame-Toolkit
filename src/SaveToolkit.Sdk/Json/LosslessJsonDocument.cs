using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk.Json;

/// <summary>
/// A bounded JSON lexical tree that retains the original UTF-8 bytes and patches only selected
/// scalar token spans. Container layout, member names, duplicate occurrences and trivia are read-only.
/// </summary>
public sealed class LosslessJsonDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] _source;
    private readonly object _identity = new();
    private readonly Dictionary<int, LosslessJsonNode> _byId;
    private readonly Dictionary<int, string> _replacements = [];

    internal LosslessJsonDocument(byte[] source, LosslessJsonLimits limits, List<LosslessJsonNode> nodes)
    {
        _source = source;
        Limits = limits;
        Nodes = new ReadOnlyCollection<LosslessJsonNode>(nodes);
        _byId = nodes.ToDictionary(node => node.Id);
        Root = nodes[0];
        Preamble = source.AsMemory(0, Root.OriginalSpan.Offset);
        Postamble = source.AsMemory(Root.OriginalSpan.End);
    }

    /// <summary>Limits used while parsing and validating replacements.</summary>
    public LosslessJsonLimits Limits { get; }
    /// <summary>Root value.</summary>
    public LosslessJsonNode Root { get; }
    /// <summary>All values in source order, including the root.</summary>
    public IReadOnlyList<LosslessJsonNode> Nodes { get; }
    /// <summary>Bytes before the root, including BOM and leading whitespace.</summary>
    public ReadOnlyMemory<byte> Preamble { get; }
    /// <summary>Bytes after the root, including trailing whitespace.</summary>
    public ReadOnlyMemory<byte> Postamble { get; }
    /// <summary>Whether at least one scalar has a replacement lexeme.</summary>
    public bool IsDirty => _replacements.Count != 0;
    /// <summary>Original input length, including BOM and whitespace.</summary>
    public int OriginalLength => _source.Length;
    /// <summary>Original input bytes, retained read-only for adapter span calculations.</summary>
    public ReadOnlyMemory<byte> OriginalBytes => _source;

    /// <summary>Parses a complete UTF-8 JSON value without normalizing any bytes.</summary>
    public static LosslessJsonDocument Parse(ReadOnlySpan<byte> bytes, LosslessJsonLimits? limits = null)
    {
        var selected = limits ?? LosslessJsonLimits.Default;
        selected.Validate();
        if (bytes.Length is < 1 || bytes.Length > selected.MaxBytes)
            throw new LosslessJsonException($"JSON input must be between 1 and {selected.MaxBytes} bytes.");
        var source = bytes.ToArray();
        try
        {
            _ = StrictUtf8.GetCharCount(source);
            return LosslessJsonParser.Parse(source, selected);
        }
        catch (DecoderFallbackException ex)
        {
            throw new LosslessJsonException($"Invalid UTF-8 at byte offset {ex.Index}.", ex.Index, ex);
        }
        catch (JsonException ex)
        {
            throw new LosslessJsonException("Invalid JSON syntax.", ex.BytePositionInLine is { } position
                ? checked((int)position) : null, ex);
        }
        catch (LosslessJsonException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            throw new LosslessJsonException("Invalid JSON string or Unicode escape.", null, ex);
        }
    }

    /// <summary>Parses a byte array; the input is copied so callers may reuse their buffer.</summary>
    public static LosslessJsonDocument Parse(byte[] bytes, LosslessJsonLimits? limits = null) => Parse(bytes.AsSpan(), limits);

    /// <summary>Finds a node by its stable parse-time identity.</summary>
    public bool TryGetNode(int id, out LosslessJsonNode? node) => _byId.TryGetValue(id, out node);

    /// <summary>Returns the exact original bytes for a node, including its original trivia-free span.</summary>
    public ReadOnlyMemory<byte> GetOriginalBytes(LosslessJsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        EnsureOwned(node);
        return _source.AsMemory(node.OriginalSpan.Offset, node.OriginalSpan.Length);
    }

    /// <summary>
    /// Replaces one scalar token with another scalar lexeme. The source span is the only changed
    /// region during serialization, so length changes do not rewrite neighboring bytes.
    /// </summary>
    /// <returns>True when the current lexeme changed; false for an exact no-op.</returns>
    public bool ReplaceScalar(LosslessJsonNode node, string scalarLexeme) => ReplaceScalar(node.Id, scalarLexeme);

    /// <inheritdoc cref="ReplaceScalar(LosslessJsonNode, string)" />
    public bool ReplaceScalar(int nodeId, string scalarLexeme)
    {
        ArgumentNullException.ThrowIfNull(scalarLexeme);
        if (!_byId.TryGetValue(nodeId, out var node)) throw new KeyNotFoundException($"Unknown JSON node {nodeId}.");
        if (!node.IsScalar) throw new InvalidOperationException("Only scalar JSON nodes can be replaced.");
        var parsed = LosslessJsonScalar.Parse(scalarLexeme, Limits);
        var current = node.CurrentLexeme!;
        if (current == scalarLexeme) return false;
        if (scalarLexeme == node.OriginalLexeme) _replacements.Remove(nodeId);
        else _replacements[nodeId] = scalarLexeme;
        node.Kind = parsed.Kind;
        node.StringValue = parsed.StringValue;
        node.BooleanValue = parsed.Kind == LosslessJsonNodeKind.Boolean && scalarLexeme == "true" ? true
            : parsed.Kind == LosslessJsonNodeKind.Boolean && scalarLexeme == "false" ? false : null;
        node.CurrentLexeme = scalarLexeme;
        return true;
    }

    /// <summary>Captures replacement lexemes for an undo or redo operation.</summary>
    public LosslessJsonSnapshot CaptureSnapshot()
    {
        var replacements = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(_replacements));
        var size = replacements.Sum(pair => 32L + pair.Key.ToString(CultureInfo.InvariantCulture).Length * 2L + pair.Value.Length * 2L);
        return new LosslessJsonSnapshot(_identity, replacements, size);
    }

    /// <summary>Restores a snapshot and returns the prior state for redo.</summary>
    public LosslessJsonSnapshot RestoreSnapshot(LosslessJsonSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ReferenceEquals(snapshot.Owner, _identity))
            throw new ArgumentException("Snapshot belongs to another JSON document.", nameof(snapshot));
        foreach (var pair in snapshot.Replacements)
        {
            if (!_byId.TryGetValue(pair.Key, out var node) || !node.IsScalar)
                throw new ArgumentException("Snapshot belongs to another JSON document.", nameof(snapshot));
            _ = LosslessJsonScalar.Parse(pair.Value, Limits);
        }
        var prior = CaptureSnapshot();
        foreach (var node in Nodes.Where(candidate => candidate.OriginalLexeme is not null))
        {
            var lexeme = snapshot.Replacements.TryGetValue(node.Id, out var replacement)
                ? replacement : node.OriginalLexeme!;
            Apply(node, lexeme);
        }
        _replacements.Clear();
        foreach (var pair in snapshot.Replacements) _replacements.Add(pair.Key, pair.Value);
        return prior;
    }

    /// <summary>Serializes original bytes plus current scalar replacements.</summary>
    public byte[] Serialize()
    {
        if (_replacements.Count == 0) return (byte[])_source.Clone();
        using var output = new MemoryStream(_source.Length);
        var cursor = 0;
        foreach (var node in Nodes.Where(candidate => _replacements.ContainsKey(candidate.Id)).OrderBy(candidate => candidate.OriginalSpan.Offset))
        {
            output.Write(_source, cursor, node.OriginalSpan.Offset - cursor);
            var replacement = Encoding.UTF8.GetBytes(_replacements[node.Id]);
            output.Write(replacement);
            cursor = node.OriginalSpan.End;
        }
        output.Write(_source, cursor, _source.Length - cursor);
        return output.ToArray();
    }

    private void Apply(LosslessJsonNode node, string lexeme)
    {
        var parsed = LosslessJsonScalar.Parse(lexeme, Limits);
        node.Kind = parsed.Kind;
        node.StringValue = parsed.StringValue;
        node.BooleanValue = parsed.Kind == LosslessJsonNodeKind.Boolean && lexeme == "true" ? true
            : parsed.Kind == LosslessJsonNodeKind.Boolean && lexeme == "false" ? false : null;
        node.CurrentLexeme = lexeme;
    }

    private void EnsureOwned(LosslessJsonNode node)
    {
        if (!_byId.TryGetValue(node.Id, out var owned) || !ReferenceEquals(node, owned))
            throw new ArgumentException("The node belongs to another JSON document.", nameof(node));
    }
}
