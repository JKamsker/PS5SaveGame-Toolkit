using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed class LsmfLiveWorkspace
{
    private readonly List<(string Prefix, Lazy<LsmfLiveSource> Source)> _sources = [];
    private readonly Dictionary<string, LsmfLiveViews> _views = new(StringComparer.Ordinal);
    private readonly Func<LsmfLiveSource, LsmfNumber, FieldNode> _bind;
    private readonly Func<string, string, FieldValue, FieldNode> _inspect;
    private readonly Func<LsmfLiveSource, LsmfSlot, FieldNode> _bindSlot;
    private readonly Bg3References _references;

    internal LsmfLiveWorkspace(IReadOnlyList<DocumentEntry> entries, Func<LsmfLiveSource, LsmfNumber, FieldNode> bind,
        Func<string, string, FieldValue, FieldNode> inspect, Func<LsmfLiveSource, LsmfSlot, FieldNode> bindSlot,
        Bg3References references)
    {
        _bind = bind;
        _inspect = inspect;
        _bindSlot = bindSlot;
        _references = references;
        for (var entry = 0; entry < entries.Count; entry++)
        {
            if (entries[entry].Resource is not { } resource) continue;
            foreach (var node in resource.Nodes.Where(n => n.Name == "NewAge"))
                foreach (var attribute in node.Attributes)
                {
                    var value = resource.Attributes[attribute];
                    if (value is not { Name: "NewAge", Type: LarianAttributeType.ScratchBuffer } ||
                        _sources.Any(s => s.Prefix == $"live/{entry}/{attribute}")) continue;
                    if (_sources.Count == 16) return;
                    var sourceEntry = entry;
                    _sources.Add(($"live/{entry}/{attribute}", new Lazy<LsmfLiveSource>(() => new(sourceEntry, attribute, value.Data, resource))));
                }
        }
    }

    internal IReadOnlyList<LsmfLiveSource> Sources => _sources.Select(source => TryRead(source.Source)).OfType<LsmfLiveSource>().ToArray();
    internal IReadOnlyList<QueryableCollection> Collections => Sources.SelectMany(source => View(source).Collections).ToArray();
    internal IReadOnlyList<ViewNode> Build() =>
    [
        new TextNode("Experimental live NewAge storage. Source rows are not party identities; all owners and epochs are included. " + LsmfLiveSource.Warning) { Tone = Tone.Warning },
        .. _sources.Select(s => (ViewNode)SectionNode.Lazy($"NewAge source {s.Prefix}", s.Prefix)),
        .. _sources.Count == 0 ? new ViewNode[] { new TextNode("No typed NewAge source is present.") } : [],
    ];

    internal IReadOnlyList<ViewNode> Expand(string id)
    {
        var source = _sources.FirstOrDefault(s => id == s.Prefix || id.StartsWith(s.Prefix + "/", StringComparison.Ordinal));
        if (source.Source is null) throw new KeyNotFoundException("Unknown NewAge source.");
        try { return View(source.Source.Value).Expand(id); }
        catch (SaveFormatException exception)
        { return [new TextNode("Unsupported live NewAge layout: " + exception.Message + " Original bytes remain available raw.") { Tone = Tone.Warning }]; }
    }

    internal QueryableCollectionPage Query(string id, QueryableCollectionQuery query)
    {
        var source = Sources.SingleOrDefault(s => id.StartsWith(s.Prefix + "/", StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("Unknown NewAge collection.");
        return View(source).Query(id, query);
    }

    internal IReadOnlyDictionary<int, uint>[] Capture() => Sources.Select(source => (IReadOnlyDictionary<int, uint>)source.Capture()).ToArray();
    internal void Restore(IReadOnlyList<IReadOnlyDictionary<int, uint>> state)
    {
        var sources = Sources;
        BinaryData.Require(sources.Count == state.Count, "NewAge undo source mismatch.");
        for (var index = 0; index < sources.Count; index++) sources[index].Restore(state[index]);
    }

    internal Dictionary<int, byte[]> Replacements(int entry) => Sources.Where(source => source.Entry == entry && source.HasEdits)
        .ToDictionary(source => source.Attribute, source => source.Write());

    private LsmfLiveViews View(LsmfLiveSource source)
    {
        if (!_views.TryGetValue(source.Prefix, out var view))
            _views.Add(source.Prefix, view = new LsmfLiveViews(source, _bind, _inspect, _bindSlot, _references));
        return view;
    }

    private static LsmfLiveSource? TryRead(Lazy<LsmfLiveSource> source)
    {
        try { return source.Value; }
        catch (SaveFormatException) { return null; }
    }
}
