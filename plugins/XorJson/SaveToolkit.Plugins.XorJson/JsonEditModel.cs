using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Json;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Owns identities independently of array positions and keeps snapshots detached from live state.</summary>
internal sealed class JsonEditModel
{
    private const int EstimatedNodeBytes = 256;
    private int _nextId;
    private readonly LosslessJsonDocument _losslessDocument;
    private Dictionary<int, JsonValueToken> _index;
    private IReadOnlyList<JsonValueToken>? _nodes;
    public JsonValueToken Root { get; private set; }
    public IReadOnlyList<JsonValueToken> Nodes => _nodes ??= Walk(Root).ToArray();

    public JsonEditModel(JsonTokenTree tree)
    {
        Root = tree.Nodes[0];
        _losslessDocument = tree.Lossless;
        _nextId = tree.Nodes.Count;
        _nodes = tree.Nodes;
        _index = tree.Nodes.ToDictionary(node => node.Index);
    }

    // The current identity index is maintained by parse, structural publication and restore.
    // Even a cold miss must not enumerate Nodes or materialize an unrelated subtree.
    public JsonValueToken Find(int id) =>
        _index.GetValueOrDefault(id) ?? throw new KeyNotFoundException("The JSON record no longer exists.");

    public void Change(string operation, int id, string? json, string? key)
    {
        var target = Find(id);
        var retired = operation == "add" ? [] : Walk(target).Select(node => node.Index).ToArray();
        JsonValueToken? inserted = null;
        if (operation == "add" && !target.IsContainer) throw new SaveFormatException("Add requires an object or array.");
        if (operation is "remove" or "replace" && target.Parent is null)
            throw new SaveFormatException("The document root cannot be removed or replaced.");
        if (operation == "add" && target.Kind == JsonTokenType.StartObject &&
            (key is null || target.Children.Any(n => n.Segment is JsonMemberPath p && p.Name == key)))
            throw new SaveFormatException("Object additions require a new, unique member name.");
        var parent = operation == "add" ? target : target.Parent!;
        var position = operation == "add" ? parent.Children.Count : parent.Children.IndexOf(target);
        if (operation == "remove") parent.Children.RemoveAt(position);
        else
        {
            inserted = CreateFragment(parent, target, operation, json, key);
            inserted.Parent = parent;
            if (operation == "replace")
            {
                inserted.Index = target.Index;
                inserted.Segment = target.Segment;
                inserted.Label = target.Label;
                parent.Children[position] = inserted;
            }
            else parent.Children.Add(inserted);
        }
        UpdateChildPaths(parent);
        foreach (var retiredId in retired) _index.Remove(retiredId);
        if (inserted is not null)
            foreach (var added in Walk(inserted)) _index[added.Index] = added;
        _nodes = null;
        // Validate the full depth, node count, size and grammar before the transaction can succeed.
        _ = JsonTokenWriter.Write(Root);
    }

    private JsonValueToken CreateFragment(JsonValueToken parent, JsonValueToken target, string operation, string? json, string? key)
    {
        if (json is null) throw new SaveFormatException("A complete JSON value is required.");
        var prefix = parent.Kind == JsonTokenType.StartObject ? JsonSerializer.Serialize(key) + ":" : "";
        if (operation == "replace")
        {
            var trivia = target.Source.AsSpan(target.LeadingOffset, target.EntryOffset - target.LeadingOffset);
            var comma = trivia.IndexOf((byte)',');
            if (comma >= 0) trivia = trivia[(comma + 1)..];
            prefix = Encoding.UTF8.GetString(trivia) + Encoding.UTF8.GetString(target.Source.AsSpan(target.EntryOffset, target.Offset - target.EntryOffset));
        }
        var wrapper = parent.Kind == JsonTokenType.StartObject ? "{" + prefix + json + "}" : "[" + prefix + json + "]";
        var fragment = JsonTokenTree.Parse(Encoding.UTF8.GetBytes(wrapper)).Nodes[0];
        if (fragment.Children.Count != 1) throw new SaveFormatException("Supply exactly one JSON value.");
        var inserted = fragment.Children[0];
        AssignIds(inserted);
        return inserted;
    }

    private static void UpdateChildPaths(JsonValueToken parent)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < parent.Children.Count; i++)
        {
            var child = parent.Children[i];
            if (parent.Kind == JsonTokenType.StartArray)
            {
                child.Segment = new JsonElementPath(i);
                child.Label = $"[{i}]";
            }
            else
            {
                var occurrence = occurrences.GetValueOrDefault(child.Label);
                child.Segment = new JsonMemberPath(child.Label, occurrence);
                occurrences[child.Label] = occurrence + 1;
            }
        }
    }

    public JsonModelSnapshot Snapshot()
    {
        var nodes = Walk(Root).ToArray();
        var sources = new Dictionary<byte[], byte[]>(ReferenceEqualityComparer.Instance);
        var snapshotRoot = Clone(Root, null, sources);
        var sourceBytes = sources.Values.Sum(b => b.LongLength);
        var textBytes = nodes.Where(n => !n.IsContainer).Sum(n => (long)n.Current.ToString().Length * sizeof(char));
        var replacements = Root.SdkDocument?.CaptureSnapshot().Replacements
            .ToDictionary(pair => pair.Key, pair => pair.Value) ?? [];
        var replacementBytes = replacements.Sum(pair => 32L + pair.Key.ToString().Length * 2L + pair.Value.Length * 2L);
        return new(snapshotRoot, _nextId,
            sourceBytes + nodes.Length * (long)EstimatedNodeBytes + textBytes + replacementBytes, replacements);
    }

    public void Restore(JsonModelSnapshot state)
    {
        Root = Clone(state.Root, null, new Dictionary<byte[], byte[]>(ReferenceEqualityComparer.Instance));
        BindSdkModel(Root);
        _ = JsonTokenWriter.Write(Root, state.LosslessReplacements);
        _index = Walk(Root).ToDictionary(node => node.Index);
        _nodes = null;
        // Never reuse an id allocated by a later, now-undone operation.
        _nextId = Math.Max(_nextId, state.NextId);
    }

    private void AssignIds(JsonValueToken node)
    {
        node.Index = _nextId++;
        foreach (var child in node.Children) AssignIds(child);
        node.OriginalChildren = node.Children.Select(n => n.Index).ToArray();
    }

    private static JsonValueToken Clone(JsonValueToken node, JsonValueToken? parent,
        Dictionary<byte[], byte[]> sources)
    {
        if (!sources.TryGetValue(node.Source, out var source))
            sources.Add(node.Source, source = node.Source.ToArray());
        var copy = new JsonValueToken(node.Index, node.Label, node.Kind, node.Offset, node.Length)
        {
            Source = source, EntryOffset = node.EntryOffset, LeadingOffset = node.LeadingOffset,
            Parent = parent, Segment = node.Segment, Spec = node.Spec, Current = node.Current,
            Original = node.Original, Warning = node.Warning, OriginalChildren = node.OriginalChildren.ToArray(),
        };
        foreach (var child in node.Children) copy.Children.Add(Clone(child, copy, sources));
        return copy;
    }

    private void BindSdkModel(JsonValueToken root)
    {
        foreach (var node in Walk(root))
        {
            node.SdkDocument = ReferenceEquals(node, root) ? _losslessDocument : null;
            node.SdkNode = null;
            if (node.Offset < 0 || node.Length < 0 || node.Offset > _losslessDocument.OriginalBytes.Length - node.Length ||
                node.Offset > node.Source.Length - node.Length)
                continue;
            if (!_losslessDocument.TryGetNode(node.Index, out var candidate) || candidate is null ||
                candidate.OriginalSpan.Offset != node.Offset || candidate.OriginalSpan.Length != node.Length ||
                !_losslessDocument.OriginalBytes.Span.Slice(node.Offset, node.Length).SequenceEqual(node.Source.AsSpan(node.Offset, node.Length)))
                continue;
            node.SdkNode = candidate;
        }
    }

    internal static IEnumerable<JsonValueToken> Walk(JsonValueToken node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child)) yield return descendant;
    }
}

internal sealed record JsonModelSnapshot(JsonValueToken Root, int NextId, long SizeBytes,
    IReadOnlyDictionary<int, string> LosslessReplacements);
