using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class GearboxGroupDocument : IGroupedSaveDocument, IQueryableDocument
{
    private readonly IReferenceContext _references;
    private readonly MemberBytes[] _original;
    private GearboxGroupMember[] _members;

    public GearboxGroupDocument(DocumentGroupInput input, IReferenceContext references)
    {
        _references = references;
        _original = input.Members.OrderBy(m => m.Role, StringComparer.Ordinal).Select(m => new MemberBytes(
            m.Role, m.File.RelativePath, m.File.Directory.TitleId, m.File.ReadAllBytes().ToArray())).ToArray();
        _members = OpenMembers(_original);
    }

    public string Title => "Gearbox character + profile (read-only)";
    public bool CanEdit => false;
    public IReadOnlyList<QueryableCollection> Collections => _members.SelectMany(m => m.Document.Collections.Select(m.Collection)).ToArray();

    public ViewNode BuildView() => new SectionNode(Title,
    [
        new TextNode("These explicitly selected files share an imported generation. This does not prove gameplay compatibility or which Guardian/account mirror wins. Backups are separate; no files are rewritten.") { Tone = Tone.Warning },
        new SectionNode("Write prerequisites", [
            new TextNode("Bank transfers and item creation/removal: disabled until controlled pickup, sale, equip and bank pairs establish capacities, indices and duplicate identity."),
            new TextNode("Serial edits: disabled until controlled level/Mayhem/Chaos/reroll pairs establish all dependent bytes. CRC and identical round trips alone do not prove an edit safe."),
            new TextNode("Guardian/Myth reconciliation: disabled until matched profile + multiple-character captures establish mirror direction, point budgets and rotation."),
            new TextNode("Write actions remain unavailable until matched before/after captures establish their effects; see the owner capture checklist."),
        ]),
        .. _members.Select(m => (ViewNode)new SectionNode(m.Role == GearboxGroups.ProfileRole ? "Shared profile / bank" : "Character progression / backpack",
            [m.Node(m.Document.BuildView())])),
    ]);

    public IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        var member = Member(lazyId);
        return member.Document.ExpandNode(member.Local(lazyId)).Select((n, i) => member.Node(n, member.Local(lazyId) + "/" + i)).ToArray();
    }

    public FieldValue GetValue(string valueId)
    {
        var member = Member(valueId);
        return member.Document.GetValue(member.Local(valueId));
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var member = Member(collectionId);
        return member.Page(member.Document.Query(member.Local(collectionId), query));
    }

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => EditOutcome.Rejected("", "Gearbox groups are read-only pending controlled write evidence.");
    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        throw new NotSupportedException("Gearbox group writes are disabled pending controlled evidence and F6 planning.");
    public IReadOnlyList<FileOutput> Serialize() => _members.SelectMany(m => m.Document.Serialize()).ToArray();
    public AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var member = Member(blobId);
        return member.Document.ReadBlob(member.Local(blobId), offset, count);
    }

    public DocumentMemento CaptureMemento() => new GroupSnapshot(_original);

    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        if (memento is not GroupSnapshot snapshot || !snapshot.Matches(_original))
            throw new InvalidOperationException("The snapshot does not belong to this Gearbox read-only input set.");
        var inverse = CaptureMemento();
        var restored = OpenMembers(snapshot.Copy());
        foreach (var member in _members) member.Document.Dispose();
        _members = restored;
        return inverse;
    }

    private GearboxGroupMember Member(string id) => _members.SingleOrDefault(m => id.StartsWith(m.Role + "/", StringComparison.Ordinal))
        ?? throw new KeyNotFoundException("Unknown Gearbox group member.");

    private GearboxGroupMember[] OpenMembers(MemberBytes[] members) => members.Select(m =>
    {
        var container = GearboxContainer.Parse(m.Bytes.ToArray(), m.Title, m.Role == GearboxGroups.ProfileRole);
        return new GearboxGroupMember(m.Role, new GearboxDocument(m.Path, m.Title, container, container.Decode(), _references));
    }).ToArray();

    public void Dispose()
    {
        foreach (var member in _members) member.Document.Dispose();
    }

    private sealed record MemberBytes(string Role, string Path, string Title, byte[] Bytes);

    // Every model is immutable at this boundary. Item IDs are allocated deterministically from the
    // unchanged ordered wire records, including duplicates; there are no insertion counters/cursors.
    // Snapshots must grow to include those identities/counters before any structural write is enabled.
    private sealed class GroupSnapshot(MemberBytes[] members) : DocumentMemento
    {
        private readonly MemberBytes[] _members = members.Select(m => m with { Bytes = m.Bytes.ToArray() }).ToArray();
        public override long SizeBytes => 128 + _members.Sum(m => 128L + m.Bytes.LongLength + 2L * (m.Role.Length + m.Path.Length + m.Title.Length));
        public MemberBytes[] Copy() => _members.Select(m => m with { Bytes = m.Bytes.ToArray() }).ToArray();
        public bool Matches(MemberBytes[] other) => _members.Length == other.Length && _members.Zip(other).All(pair =>
            pair.First.Role == pair.Second.Role && pair.First.Path == pair.Second.Path && pair.First.Title == pair.Second.Title &&
            pair.First.Bytes.AsSpan().SequenceEqual(pair.Second.Bytes));
    }
}
