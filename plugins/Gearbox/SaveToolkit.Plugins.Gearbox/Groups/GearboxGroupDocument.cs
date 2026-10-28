using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class GearboxGroupDocument : IGroupedSaveDocument, IQueryableDocument, IActionPlanningDocument
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

    public string Title => "Gearbox character + profile";
    public bool CanEdit => true;
    public IReadOnlyList<QueryableCollection> Collections => _members.SelectMany(m => m.Document.Collections.Select(m.Collection)).ToArray();

    public ViewNode BuildView() => new SectionNode(Title,
    [
        new TextNode("These explicitly selected files share an imported generation. This does not prove gameplay compatibility or which Guardian/account mirror wins. Backups are separate. " + SerialWrites.Warning) { Tone = Tone.Warning },
        .. GearboxTransferActions.Build(),
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

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var before = CaptureMemento();
        var changes = new List<ValueChange>();
        try
        {
            foreach (var memberEdits in edits.GroupBy(e => Member(e.ValueId)))
            {
                var member = memberEdits.Key;
                var outcome = member.Document.ApplyEdits(memberEdits.Select(e => new ValueEdit(member.Local(e.ValueId), e.Value)).ToArray());
                if (!outcome.Succeeded)
                {
                    _ = RestoreMemento(before);
                    return new() { Errors = outcome.Errors.Select(e => e with { ValueId = member.Prefix(e.ValueId) }).ToArray() };
                }
                changes.AddRange(outcome.Changes.Select(c => c with { ValueId = member.Prefix(c.ValueId) }));
            }
            _ = Serialize();
            return new() { Changes = changes, Revert = changes.Count > 0 ? before : null, StructureChanged = changes.Count > 0, Warnings = [SerialWrites.Warning] };
        }
        catch
        {
            _ = RestoreMemento(before);
            throw;
        }
    }

    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var member = Member(actionId);
        return member.Document.InvokeAction(member.Local(actionId), parameters);
    }

    internal GearboxDocument Character => _members.Single(m => m.Role == GearboxGroups.CharacterRole).Document;
    internal GearboxDocument Profile => _members.Single(m => m.Role == GearboxGroups.ProfileRole).Document;

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId.StartsWith("transfer/", StringComparison.Ordinal)) return new GearboxTransferActions(this).Plan(actionId, scope);
        var member = Member(actionId);
        var plan = member.Document.PlanAction(member.Local(actionId), parameters, LocalScope(member, scope));
        return plan with { Changes = plan.Changes.Select(c => c with { ValueId = member.Prefix(c.ValueId) }).ToArray(),
            SkippedRows = plan.SkippedRows.Select(s => s with { RowKey = member.Prefix(s.RowKey) }).ToArray() };
    }

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var fresh = PlanAction(actionId, parameters, scope);
        if (fresh.Errors.Count > 0) return new() { Errors = fresh.Errors };
        if (fresh.Changes.Count == 0) return new() { Warnings = fresh.Warnings };
        var before = CaptureMemento();
        try
        {
            if (actionId.StartsWith("transfer/", StringComparison.Ordinal)) new GearboxTransferActions(this).Apply(actionId, scope);
            else
            {
                var member = Member(actionId);
                var outcome = member.Document.ApplyActionPlan(member.Local(actionId), parameters, LocalScope(member, scope), plan);
                if (!outcome.Succeeded) { _ = RestoreMemento(before); return outcome; }
            }
            _ = Serialize();
            return new() { StructureChanged = true, Revert = before, Warnings = fresh.Warnings,
                Changes = fresh.Changes.Select(c => new ValueChange(c.ValueId, c.OldValue, c.NewValue)).ToArray() };
        }
        catch { _ = RestoreMemento(before); throw; }
    }

    private static ActionScope LocalScope(GearboxGroupMember member, ActionScope scope) => new(scope.RowKeys?.Select(key =>
    {
        var local = member.Local(key);
        var entry = member.Document.Inventory.Entries.SingleOrDefault(e => local == e.List + "/" + e.Id)
            ?? throw new SaveFormatException("The selection is not an existing item row in this member.");
        if (scope.QueryScope is { } query && member.Local(query.CollectionId) != entry.List)
            throw new SaveFormatException("The resolved item selection belongs to another collection.");
        return entry.Id;
    }).ToArray()) { QueryScope = scope.QueryScope is { } query ? query with { CollectionId = member.Local(query.CollectionId) } : null };
    public IReadOnlyList<FileOutput> Serialize() => _members.SelectMany(m => m.Document.Serialize()).ToArray();
    public AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var member = Member(blobId);
        return member.Document.ReadBlob(member.Local(blobId), offset, count);
    }

    public DocumentMemento CaptureMemento() => new GroupSnapshot(_original, _members.ToDictionary(m => m.Role, m => m.Document.CaptureState(), StringComparer.Ordinal));

    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        if (memento is not GroupSnapshot snapshot || !snapshot.Matches(_original))
            throw new InvalidOperationException("The snapshot does not belong to this Gearbox input set.");
        var inverse = CaptureMemento();
        var restored = OpenMembers(snapshot.Copy());
        foreach (var member in restored) _ = member.Document.RestoreMemento(snapshot.States[member.Role]);
        foreach (var member in _members) member.Document.Dispose();
        _members = restored;
        return inverse;
    }

    private GearboxGroupMember Member(string id) => _members.SingleOrDefault(m => id.StartsWith(m.Role + "/", StringComparison.Ordinal))
        ?? throw new KeyNotFoundException("Unknown Gearbox group member.");

    private GearboxGroupMember[] OpenMembers(MemberBytes[] members) => members.Select(m =>
    {
        var container = GearboxContainer.Parse(m.Bytes.ToArray(), m.Title, m.Role == GearboxGroups.ProfileRole);
        return new GearboxGroupMember(m.Role, new GearboxDocument(m.Path, m.Title, container, container.Decode(), _references) { Coordinated = true });
    }).ToArray();

    public void Dispose()
    {
        foreach (var member in _members) member.Document.Dispose();
    }

    private sealed record MemberBytes(string Role, string Path, string Title, byte[] Bytes);

    private sealed class GroupSnapshot(MemberBytes[] members, Dictionary<string, DocumentMemento> states) : DocumentMemento
    {
        public IReadOnlyDictionary<string, DocumentMemento> States { get; } = states;
        private readonly MemberBytes[] _members = members.Select(m => m with { Bytes = m.Bytes.ToArray() }).ToArray();
        public override long SizeBytes => 128 + States.Values.Sum(s => s.SizeBytes) + _members.Sum(m => 128L + m.Bytes.LongLength + 2L * (m.Role.Length + m.Path.Length + m.Title.Length));
        public MemberBytes[] Copy() => _members.Select(m => m with { Bytes = m.Bytes.ToArray() }).ToArray();
        public bool Matches(MemberBytes[] other) => _members.Length == other.Length && _members.Zip(other).All(pair =>
            pair.First.Role == pair.Second.Role && pair.First.Path == pair.Second.Path && pair.First.Title == pair.Second.Title &&
            pair.First.Bytes.AsSpan().SequenceEqual(pair.Second.Bytes));
    }
}
