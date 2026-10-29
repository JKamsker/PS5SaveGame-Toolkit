using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Owns identities independently of array positions and keeps snapshots detached from live state.</summary>
internal sealed class JsonEditModel
{
    private const int EstimatedNodeBytes = 256;
    private int _nextId;
    private Dictionary<int, JsonValueToken>? _index;
    private IReadOnlyList<JsonValueToken>? _nodes;
    public JsonValueToken Root { get; private set; }
    public IReadOnlyList<JsonValueToken> Nodes => _nodes ??= Walk(Root).ToArray();
    private Dictionary<int, JsonValueToken> Index => _index ??= Nodes.ToDictionary(node => node.Index);

    public JsonEditModel(JsonTokenTree tree)
    {
        Root = tree.Nodes[0];
        _nextId = tree.Nodes.Count;
        _nodes = tree.Nodes;
    }

    public JsonValueToken Find(int id)
    {
        // Parsed IDs are their positions until structure changes. Reuse that index
        // without building another document-wide dictionary for a single lookup.
        if (_nodes is { } nodes && id >= 0 && id < nodes.Count && nodes[id].Index == id) return nodes[id];
        return Index.GetValueOrDefault(id) ?? throw new KeyNotFoundException("The JSON record no longer exists.");
    }

    public void Change(string operation, int id, string? json, string? key)
    {
        var target = Find(id);
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
            var inserted = CreateFragment(parent, target, operation, json, key);
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
        _index = null;
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
        var sourceBytes = nodes.Select(n => n.Source).Distinct().Sum(b => b.LongLength);
        var textBytes = nodes.Where(n => !n.IsContainer).Sum(n => (long)n.Current.ToString().Length * sizeof(char));
        return new(Clone(Root, null), _nextId, sourceBytes + nodes.Length * (long)EstimatedNodeBytes + textBytes);
    }

    public void Restore(JsonModelSnapshot state)
    {
        Root = Clone(state.Root, null);
        _index = null;
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

    private static JsonValueToken Clone(JsonValueToken node, JsonValueToken? parent)
    {
        var copy = new JsonValueToken(node.Index, node.Label, node.Kind, node.Offset, node.Length)
        {
            Source = node.Source, EntryOffset = node.EntryOffset, LeadingOffset = node.LeadingOffset,
            Parent = parent, Segment = node.Segment, Spec = node.Spec, Current = node.Current,
            Original = node.Original, Warning = node.Warning, OriginalChildren = node.OriginalChildren.ToArray(),
        };
        foreach (var child in node.Children) copy.Children.Add(Clone(child, copy));
        return copy;
    }

    internal static IEnumerable<JsonValueToken> Walk(JsonValueToken node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child)) yield return descendant;
    }
}

internal sealed record JsonModelSnapshot(JsonValueToken Root, int NextId, long SizeBytes);
